# Localization Architecture

## 1. Purpose

This document is the authoritative reference for localization (i18n) across the FluxCommerce platform.

It defines the culture strategy, the culture resolution pipeline, the resource organization, the naming conventions, and the conventions every module must follow when producing user-facing text.

Phase 1.6 establishes this foundation. Multilingual **business data** (Product translations, Category translations, Brand translations, Attribute translations, SEO, search localization, and translation management) is intentionally **out of scope** and belongs to Phase 2 (Catalog).

---

## 2. Design Goals

- **One default culture, always working.** Every request resolves to a valid culture, and missing translations fall back to the default language instead of failing.
- **Consistent culture resolution.** The same resolution rules apply to every endpoint and every module.
- **Standard HTTP behavior.** Culture is negotiated through standard mechanisms (`Accept-Language` header) and advertised through standard response headers (`Content-Language`).
- **Module ownership of strings.** Each module owns the user-facing strings of its own functionality. Shared cross-cutting text lives in a shared resource set, not inside modules.
- **Framework-independent module abstractions.** Modules must never depend on ASP.NET Core localization implementation details. They depend on shared abstractions only (established in SubPhase 1.6.4).
- **Stable machine-readable error codes.** `Error.Code` values are stable keys; localized display text is resolved separately from the machine contract.
- **Extensible culture set.** Adding a culture must be a small, mechanical change (registry entry + satellite resources).

---

## 3. Supported Culture Strategy

The supported culture set is defined in the API host in `src/FluxCommerce.Api/Extensions/LocalizationConfiguration.cs`.

| Culture | Language | Direction | Notes |
|---------|----------|-----------|-------|
| `en` | English | LTR | Default culture |
| `fr` | French | LTR | |
| `de` | German | LTR | |
| `es` | Spanish | LTR | |
| `ar` | Arabic | RTL | Exercises the RTL strategy |

Rules:

- Cultures are **neutral** (`en`, `fr`), not region-specific (`en-US`). Region-specific cultures can be added later when a business need exists (e.g., `pt-BR`).
- The culture set is defined in code as a single source of truth. Adding a culture requires: (1) adding it to the code list, (2) adding satellite resource files, (3) updating the supported-culture table in this document.
- Configuration-driven cultures are a future option and are deliberately not implemented yet.

---

## 4. Default Culture Strategy

- The default culture is **`en`** (English).
- `en` is both the **default request culture** and the **fallback language** for missing translations.
- The default matches the identity profile default (`User.DefaultPreferredLanguage = "en"`, `User.cs`).
- When no request signal is present, or when the requested culture is unsupported, the request culture falls back to `en`.

---

## 5. Culture Resolution Strategy

The request culture is resolved by ASP.NET Core `RequestLocalizationMiddleware`, configured in `LocalizationConfiguration` and registered first in the API pipeline.

Resolution order:

1. **Query string** — `?culture=fr&ui-culture=fr` (`QueryStringRequestCultureProvider`). Explicit and testable; used for development and future storefront switching.
2. **`Accept-Language` header** — standard HTTP content negotiation (`AcceptLanguageHeaderRequestCultureProvider`).
3. **Default culture** — `en`, applied when no provider produces a supported culture.

Design decisions:

- The **cookie provider is excluded** (`CookieRequestCultureProvider` is not registered). Culture persistence via cookies is deferred until a client-side persistence strategy is designed.
- Parent-culture fallback (`fr-FR` → `fr`) remains enabled through the ASP.NET Core default (`FallBackToParentCultures`).
- Unsupported or malformed cultures are ignored and the next provider is consulted; ultimately the default culture applies.
- **`Content-Language` response header**: enabled via `ApplyCurrentCultureToResponseHeaders = true`. Every response advertises the culture that was actually used.
- **Authenticated profile override** (resolving culture from `User.PreferredLanguage`) is a documented future evolution. It requires pipeline access to identity data and is deferred.

---

## 6. RequestLocalization Configuration (Implemented)

Implemented in SubPhase 1.6.2:

| Setting | Value |
|---------|-------|
| Default culture | `en` |
| Supported cultures | `en`, `fr`, `de`, `es`, `ar` |
| Supported UI cultures | `en`, `fr`, `de`, `es`, `ar` |
| Providers (in order) | `QueryStringRequestCultureProvider`, `AcceptLanguageHeaderRequestCultureProvider` |
| `Content-Language` header | Enabled |
| Localization services | `AddLocalization()` (framework string localizer registration) |
| Middleware placement | First middleware in the pipeline, before exception handling |

---

## 7. Resource Organization Strategy

Resources are organized in two tiers:

### 7.1 Shared Resource Set (cross-cutting)

Contains strings shared by all modules:

- Validation messages
- Common errors
- Authentication / authorization messages
- Common / generic messages
- Success messages

The shared set is hosted in a shared project that all module layers can reference. The hosting project decision follows `ADR-003` (BuildingBlocks criteria) and is finalized in SubPhases 1.6.3/1.6.4 together with the framework-independent localization abstractions.

### 7.2 Module Resource Sets (module-owned)

Each module owns the strings of its own functionality:

- Identity: login, registration, verification, password flows, security messages
- Future modules: catalog, cart, orders, inventory, payments, notifications

Module resources live inside the module project that produces the strings (Application layer for command/validation messages, Infrastructure layer for email templates).

---

## 8. Resource Naming Conventions

- **File naming:** `<ResourceSetName>.resx` for the neutral (default-language) resource, `<ResourceSetName>.<culture>.resx` for translations.

  ```
  IdentityResources.resx          (English default)
  IdentityResources.fr.resx
  IdentityResources.de.resx
  ```

- **Key naming:** PascalCase segments joined by dots. The first segment is the area; the last segment is the specific message.

  ```
  Validation.Email.Required
  Errors.Identity.InvalidCredentials
  Email.Verification.Subject
  ```

- **Module keys** are prefixed with the module name (`Identity.*`, `Catalog.*`).
- **Culture codes** are lowercase neutral names (`fr`, `de`, `es`, `ar`); region suffixes are added only when required (`pt-BR`).
- **Fallback:** culture-specific resource → parent culture → neutral resource (English) → key passthrough (the key itself is returned so missing strings are visible rather than silently broken).

---

## 9. Folder Structure Conventions

```
src/
├── BuildingBlocks/
│   └── <Shared hosting project>/
│       └── Resources/
│           ├── SharedResources.resx
│           ├── SharedResources.fr.resx
│           └── ...
└── Modules/
    └── <Module>/
        ├── <Layer>/                            (Application / Infrastructure / ...)
        │   └── Resources/
        │       ├── <Module>Resources.resx
        │       └── <Module>Resources.<culture>.resx
        └── ...
```

- The `Resources` folder sits at the root of the project that owns the strings.
- One resource set per project area; the set name matches the area (e.g., `IdentityValidationResources`, `EmailResources`).

---

## 10. RTL/LTR Strategy

- `ar` (Arabic) is a supported RTL culture; the remaining supported cultures are LTR.
- **API responsibility:** propagate the culture and provide directionality metadata. The direction is derived from the resolved culture (`CultureInfo.TextInfo.IsRightToLeft`) — no separate flag is stored or transmitted.
- **Response header:** `Content-Language` advertises the resolved culture so clients can determine directionality.
- **Frontend responsibility:** actual RTL rendering (mirrored layouts, right-aligned text) is an Angular client concern outside the backend's scope.
- **Data:** user-generated and business content is stored without directionality assumptions; directionality is a presentation concern applied at render time.

---

## 11. Module Localization Conventions

Every module that produces user-facing text must follow these rules:

1. **No inline literals.** User-facing strings are placed in the module's resource set (or the shared set for cross-cutting categories) and resolved through the shared localization abstractions.
2. **Abstractions only.** Modules depend on the framework-independent localization abstractions (SubPhase 1.6.4), never directly on ASP.NET Core localization implementation types.
3. **Stable keys.** Localization keys are stable across releases; they are a contract (this includes `Error.Code` values).
4. **Machine-readable errors stay machine-readable.** `Error.Code` and ProblemDetails structure do not change when display text is localized; only the displayed text changes with culture.
5. **Culture is never hardcoded.** Modules must not construct cultures themselves; they read the current culture through the abstraction.
6. **Business translations are not module-localization concerns.** Multilingual business entities (products, categories, brands, attributes) are Phase 2 scope.

---

## 12. Future Extensibility Guidelines

- **Adding a culture:** update the supported-cultures list in `LocalizationConfiguration`, add satellite resources for the culture, update this document's table, and add test coverage for resolution and fallback.
- **Region-specific cultures:** add a region suffix only when the neutral culture is insufficient (e.g., `pt-BR` vs `pt`).
- **Client persistence:** a cookie provider or storage-backed culture provider can be introduced when a persistence strategy is designed.
- **Profile-based culture:** `User.PreferredLanguage` exists in the identity model and can drive per-user culture once an authenticated culture provider is implemented.
- **Framework-independent abstractions:** module-facing abstractions (`ILocalizer`, current-culture access) are introduced in SubPhase 1.6.4 per the phase roadmap; the implementation remains replaceable behind them.
- **Multilingual business data:** Phase 2 (Catalog) introduces translation entities; the localization foundation defined here is the mechanism those translations are resolved through.

---

## 13. Deferred Items (not implemented by this phase)

| Item | SubPhase |
|------|----------|
| Resource files (`.resx`) and shared resources | 1.6.3 |
| Framework-independent localization abstractions | 1.6.4 |
| Validation localization | 1.6.5 |
| ProblemDetails localization | 1.6.6 |
| Identity localization | 1.6.7 |
| Email localization | 1.6.8 |
| Swagger localization support | 1.6.9 |
| Testing & verification of localization | 1.6.10 |
| Product / category / brand / attribute translations | Phase 2 |

---

## 14. References

- `docs/phases/PHASE_1_6_INTERNATIONALIZATION_I18N.md` — phase specification
- `docs/adr/ADR-006-Localization-Architecture.md` — architectural decision record
- `src/FluxCommerce.Api/Extensions/LocalizationConfiguration.cs` — implemented configuration
- `src/FluxCommerce.Api/Extensions/PipelineConfiguration.cs` — middleware placement
