# Phase 23 — Dashboard Data Completion — Implementation Plan

**Status:** Planned (user-confirmed scope from the dashboard data audit, 2026-10-05)
**Prerequisite:** Phase 22 E1–E3 complete (adaptive dashboard, catalog counts, `users.read`).
**Scope:** Dashboard-visible data gaps only. No auth-model, saga, or persistence-ownership changes.

---

## 1. Problem (Dashboard Data Audit, 2026-10-05)

The dashboard faithfully delivers 100% of what its sources provide (verified field-by-field:
all counts, all stats, 5 recent orders). The gaps are data the *system holds* but no dashboard
surface sends:

| # | Gap | Where the data lives | Size |
|---|---|---|---|
| 1 | **Refunded amount missing** — stats carry the refunded *count* and *succeeded* totals per currency, so net revenue is not computable | `Payments` table (`Amount`+`Currency`+`Status`) | Small |
| 2 | **No recent payments / recent users** — only recent *orders* exist | Existing list queries (`IPaymentAdminStore.GetPageAsync`, `IUserAdminStore.GetPageAsync`) | Small |
| 3 | **No trends** — per-day orders/revenue for charts | `Orders`/`Payments` `CreatedAtUtc` + amounts | Medium (portability risk) |
| 4 | **No operational health** — stuck outbox messages, DLQ depth, aging sagas | Per-module `OutboxMessages`, broker DLQ, `OrderSagas` state | Large — separate feature |

## 2. Goals

1. Net revenue becomes computable from one snapshot (refunded totals per currency next to
   succeeded totals).
2. Recent-payments and recent-users widgets work without extra round trips (same newest-5 pattern
   as recent orders, same gates).
3. Daily series for orders + revenue over a bounded window, portable across SQLite/SQL Server.
4. Explicitly **not** in this phase: broker DLQ depths (broker-side, not DB) and a full ops-health
   surface — recorded as a follow-up track in §8.

## 3. Non-Goals

- No new permissions (existing gates cover everything: stats section keeps its gate, lists keep
  theirs; refunded totals ride inside the stats section).
- No change to list/count endpoints, auth model, token shape, or refresh boundary.
- No per-entity catalog splits, no dashboard-section splits beyond §5.

## 4. Design

### F1 — Refunded Totals per Currency (Payments Module)

- Extend `PaymentAdminStatsReadModel` / `PaymentAdminStatsResponse.TotalsByCurrency` semantics:
  add `RefundedTotalsByCurrency: IReadOnlyList<PaymentCurrencyTotal…>` computed by a second
  `GroupBy Currency` over `Status == Refunded` (same translation-safe shape as the existing
  succeeded grouping — `CountAsync`/`SumAsync` over scalar predicates, proven on both providers).
- Additive contract change only (`PaymentAdminStatsResponse` gains one field); existing consumers
  ignore unknown fields per the forward-compatibility rule (`ENUM_REFERENCE.md` §8).
- Handler mapping extended; no DI changes (same store/handler).

### F2 — Recent Payments + Recent Users (Host Composition Only)

- No new module code: the composer calls the *existing* `IPaymentAdminStore.GetPageAsync` and
  `IUserAdminStore.GetPageAsync` with no filters, page 1, size 5 (both already order newest-first).
- `AdminDashboardResponse` gains `RecentPayments: PaymentAdminResponse[]` (empty without
  `payments.read` — note: stats-only viewers get stats but no list, mirroring the orders
  read/list split) and `RecentUsers: UserAdminResponse[]` (empty without `users.read`).
- Same null-vs-empty convention as recent orders (`[]` when unauthorized, never 403).

### F3 — Daily Series (Bounded, Provider-Portable)

- New lightweight projections (no aggregates): last N days (default 30, max 90) of
  `(CreatedAtUtc, GrandTotal?)` for orders and `(CreatedAtUtc, Amount, Currency, Status)` for
  payments, streamed via the existing window/key patterns; **day-bucketing runs in memory**
  (`DateTimeOffset.UtcDateTime.Date`), deliberately avoiding `GROUP BY` on date parts, whose
  translation differs between SQLite (`strftime`) and SQL Server (`CAST/CONVERT`).
- Response: `DailySeries: [{ date: "2026-10-01", orders: int, revenue: { currency, amount }[] }]`
  (multi-currency preserved as arrays, same convention as `totalsByCurrency`).
- Cost is bounded by construction (sliding window, lightweight rows, admin-only traffic); the
  tradeoff is documented here instead of hidden behind provider-specific SQL.

## 5. Section → Entitlement Mapping (Additions)

| Dashboard addition | Included iff | Notes |
|---|---|---|
| `refundedTotals` (inside payments) | `payments.read` **or** `payments.stats` | Same gate as the section; additive field |
| `recentPayments` | `payments.read` | Stats-only viewers do not get the list |
| `recentUsers` | `users.read` | Same gate as the users section |
| `dailySeries` | `orders.read` **or** `payments.read`/`stats` | Series merges both sources; included if either side is entitled, with the unauthorized side zeroed (not omitted — shape stays stable for charts) |

## 6. Implementation Sub-Phases

### F1 — Refunded Totals

- Store grouping + read-model/response fields + handler mapping + `DTO_REFERENCE.md` §6 +
  `API_CONTRACT.md` §6.5/§8 rows.
- Tests: seed one `Succeeded` + one `Refunded` payment (SQL Server pattern from `MyPaymentsTests`)
  → stats show both totals; handler unit test for the mapping.
- Gates: build `0W/0E`, arch green, unit green, targeted integration green.

### F2 — Recent Lists

- Composer-only change + response fields + `DTO_REFERENCE.md` §8 + `API_CONTRACT.md` §8.
- Tests: extend `AdminDashboardTests` (payments-reader sees recent payments but no users section;
  users-reader sees recent users but no payments; unauthorized sees `[]`).
- Gates: same as F1. No migration, no DI changes (existing handlers/stores).

### F3 — Daily Series

- Two bounded projections (orders + payments, last N days) + in-memory bucketing + response shape
  + `DTO_REFERENCE.md` §8 + `API_CONTRACT.md` §8 (`days?` param, default 30, max 90).
- Tests: SQLite translation test (store-level, like `CatalogCountsStoreTests`) + dashboard
  integration asserting bucket math on seeded multi-day data (both providers: SQLite via
  Checkout factory for orders; SQL Server seed pattern for payments).
- Gates: same as F1. Explicit non-goal enforced in review: no raw provider-specific SQL.

## 7. Verification Matrix (Per Sub-Phase)

| Suite | Gate |
|---|---|
| Build | `0W/0E` (`TreatWarningsAsErrors`) |
| Architecture | green (no new boundary crossings: host composes module reads only) |
| Unit | green (mapping/bucketing logic) |
| Integration (targeted) | green (new dashboard assertions + neighbors) |

## 8. Follow-Ups (Not in Scope)

- **Ops-health track (from audit item 4):** outbox pending/failed depths per module, DLQ sizes
  (broker API, new client), aging-saga watchlist (`WaitingForPayment` older than X). Needs its own
  design: new read endpoints per module + a composed `admin/health` surface + alerting policy.
- **Refund list endpoint** (`GET /admin/payments?status=Refunded` already covers discovery via
  the existing list — no work needed, noted for completeness).
- **Time-window pushdown:** if the 90-day in-memory bucketing ever shows cost pressure, revisit
  with provider-specific SQL behind per-provider store branches (explicitly rejected for now).

## 9. Effort Estimate

| Sub-phase | New files | Modified files | Tests |
|---|---|---|---|
| F1 | 0 (extend 3) | Stats store/read-model/response, handler, 2 contract docs | +~4 |
| F2 | 0 | Dashboard composer + response, 2 contract docs | +~3 (extend `AdminDashboardTests`) |
| F3 | ~5 (2 projections, bucketing, response, tests) | Composer, DI (if new stores), 2 contract docs | +~8 |

Order F1→F2→F3 is dependency-safe (F1/F2 independent; F3 last as the only one with new queries);
each ships green independently.
