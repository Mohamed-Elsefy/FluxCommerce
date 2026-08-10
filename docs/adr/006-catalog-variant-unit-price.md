# ADR-006: Catalog Variant Unit Price as the Authority for Orders Pricing

## Status

Accepted

## Context

Catalog variants previously had no concept of a price. The Cart module stores a temporary price snapshot (UnitPrice, Currency) taken from Catalog at add-time, but `PROJECT_CONTEXT.md` §6.5 and `PHASE_3_CART_MODULE.md` §13 define that snapshot as **temporary and non-authoritative**: "Cart price is **not** the final source of truth for Orders." Checkout must recalculate prices from current Catalog data before building the final Order snapshot (`PROJECT_CONTEXT.md` §6.5, `PHASE_4_ORDERS_CHECKOUT.md` §12).

Phase 4 Checkout requires a per-line unit price to compute `Unit Price × Quantity = Line Total` and `Σ Line Totals = Subtotal`, with `GrandTotal = Subtotal` for this phase (`PHASE_4_ORDERS_CHECKOUT.md` §11). The current Catalog price must be resolved at checkout, and a price change between Cart creation and Checkout must be reflected in the Order rather than inherited from the Cart stale snapshot (`PHASE_4_ORDERS_CHECKOUT.md` §4.6, §12).

Monetary values must use a `Money` Value Object (`PHASE_4_ORDERS_CHECKOUT.md` §10): `decimal` only, required currency, no mixing of currencies, explicit SQL precision, centralized deterministic arithmetic. Money VOs already exist in the Cart and Orders domains, but the Catalog domain lacked one, so the source of the authoritative price had nowhere to hold it.

The documentation references:

- `PROJECT_CONTEXT.md` §6.5 — Pricing responsibilities include Base Price, Currency, and Price Calculation; "Prefer a Money concept"; Cart price is not the final source of truth; Checkout recalculates the current product price and creates an immutable Order snapshot.
- `PROJECT_CONTEXT.md` §4.6 and `PHASE_4_ORDERS_CHECKOUT.md` §10 — Money value object rules (decimal, required currency, consistent currency, explicit SQL precision, centralized arithmetic).
- `PHASE_4_ORDERS_CHECKOUT.md` §11 — Pricing scope: `Unit Price × Quantity = Line Total`, `Σ Line Totals = Subtotal`, `GrandTotal = Subtotal`; must remain extensible without an Order aggregate redesign.
- `PHASE_4_ORDERS_CHECKOUT.md` §12 — Orders requests current Catalog information through an explicit batch contract; Cart price/product snapshots are not authoritative.
- `PHASE_4_ORDERS_CHECKOUT.md` §4.6 — Implement current-price resolution, recalculate line totals, calculate subtotal, create an immutable Order snapshot, handle price changes between Cart creation and Checkout, add pricing/snapshot tests.

## Decision

FluxCommerce makes the Catalog **Variant** the single authoritative holder of the current unit price, expressed as a `Money` Value Object.

This decision is scoped to Phase 4. For the current implementation, Catalog Variant is the authoritative source of the current unit price. A future Pricing capability/module may extend or supersede this responsibility when pricing history, promotions, taxes, dynamic pricing, or other advanced pricing rules become part of the system.

- **`Money` Value Object in Catalog.Domain** (`ValueObjects/Money.cs`): an `Amount` (`decimal`) plus a required `Currency`. It is the domain-level monetary value object of the Catalog module, mirroring the established Money model in Cart and Orders (`PHASE_4_ORDERS_CHECKOUT.md` §10 rules are honored).
- **Variant owns `UnitPrice`**: the Variant aggregate (`Aggregates/Variant.cs`) holds `UnitPrice` as an owned `Money` value, configured in EF Core as two columns — `UnitPriceAmount` (`decimal(18,6)`, explicit precision/scale) and `UnitPriceCurrency` (`nvarchar(3)`) — through `VariantConfiguration`.
- **Price is set at variant creation**: the create commands (`CreateProductVariantCommand` and the bulk `CreateProductVariantsCommand`) require `UnitPriceAmount` and `Currency` for every variant, validated by `VariantRules` (positive amount, required 3-letter currency code). The Catalog controllers, request DTOs, and command mappers propagate the fields through the existing convention. `UpdateProductVariant` remains unchanged in this phase, so the current price is only set at creation.
- **Catalog contract exposes the current price**: the checkout revalidation contract `CatalogVariantCheckoutInfo` gained `UnitPriceAmount` and `Currency`, and `ICatalogVariantCheckoutLookup` resolves the current price per variant, reducing any chance of a stale Cart snapshot being treated as authoritative.
- **Checkout pricing authority**: `CheckoutOrderSnapshotBuilder` (Orders Application) consumes the current Catalog price via `CatalogVariantCheckoutInfo`, computes line totals (`UnitPrice × Quantity`), the subtotal (`Σ Line Totals`), and sets phase totals (`Discount = 0`, `Tax = 0`, `Shipping = 0`, `GrandTotal = Subtotal`). The Cart price snapshot is never used to compute Order values — this directly implements `PHASE_4_ORDERS_CHECKOUT.md` §11-§12.
- **Immutable Order snapshot**: the Order aggregate captures line items as an immutable snapshot built once at checkout; subsequently changing a Catalog price does not alter an existing Order (`PHASE_4_ORDERS_CHECKOUT.md` §8.5).
- **Currency consistency**: line items must share a single currency; mixed currencies fail checkout with a typed error. Existing records are backfilled via migration (all pre-existing rows received `49.99 USD`) so the dev database was migrated in place.
- **Migration `AddVariantUnitPrice`**: adds the two `UnitPrice*` columns, backfills legacy rows, and the Catalog seed passes a real price, keeping the dev (`FluxCommerce_Catalog`) database consistent with the new model. The `49.99 USD` backfill value is migration-only development data and is not a business default price.

## Consequences

Positive:

- Catalog becomes the authoritative source of the current unit price, satisfying the "Cart price is not the source of truth" invariant.
- Checkout pricing is computed from fresh Catalog data, so price changes between Cart add-time and Checkout are correctly reflected in the Order.
- Money rules (decimal, required currency, explicit precision, centralized arithmetic) are enforced consistently in the Catalog domain, matching the Cart and Orders Money model.
- The snapshot/eligibility contract extension (`ICatalogVariantCheckoutLookup`) keeps Orders decoupled from Catalog infrastructure while enabling batch price revalidation.
- Future pricing capabilities (discounts, tax, shipping, promotions) can contribute to Checkout without an Order aggregate redesign, because the Order stores its own immutable snapshot.

Negative:

- Variant creation now rejects requests without a valid price, a breaking change to the existing create endpoints (bulk + single) and their contracts.
- Price is only set at creation; updating the price of an existing variant is not yet exposed through the update endpoint (intentional scope — a variant lifecycle/pricing-update decision is deferred).
- The backfill migration bakes a single default price (`49.99 USD`) into legacy rows, which is fine for development data but must be revisited for real data before any production migration.
- Every Catalog variant now carries two extra persisted columns and must always provide a valid Money value, adding a small set-up cost to variant creation.

## Alternatives Considered

- **Keep pricing out of Catalog and store it in Orders/Checkout only** — rejected: `PROJECT_CONTEXT.md` requires recalculating at Checkout from current product data; without a Catalog-side price there is no "current" price to resolve. The Cart is explicitly not authoritative, so it cannot be the source.
- **Pricing as a separate Price/Pricing aggregate or separate table** — deferred: it is more appropriate once discounts/taxes/pricing history (Price History) are in scope; for Phase 4 the `Money` owned type on Variant provides the minimal authoritative source with full extensibility.
- **Use `double`/`float` for monetary amounts** — rejected, in line with `PHASE_4_ORDERS_CHECKOUT.md` §10: monetary values must use `decimal` with explicitly configured SQL precision.
- **Expose price only at the API layer (no domain/EF change)** — rejected: the domain would have no canonical price, EF persistence could not store it consistently, and the batch checkout contract could not carry authoritative current prices; `ICatalogVariantCheckoutLookup` needs a real price source.