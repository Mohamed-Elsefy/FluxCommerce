# Phase 1.6 — Internationalization (i18n) Foundation

## Overview

Phase 1.6 establishes the internationalization infrastructure for the platform.

The objective of this phase is **not** to implement multilingual business data (Products, Categories, Brands, etc.), but to build the shared localization foundation that every module will use.

After completing this phase, all future modules can support multiple languages without architectural changes.

---

# Objectives

This phase is responsible for:

- Establishing the localization architecture.
- Configuring ASP.NET Core localization.
- Supporting multiple cultures.
- Localizing validation messages.
- Localizing API error responses.
- Localizing Identity messages.
- Localizing Email templates.
- Providing shared localization abstractions.
- Preparing the platform for multilingual business modules.

This phase is **not** responsible for translating Catalog or any business entities.

---

# Scope

Included:

- Localization infrastructure
- Resource management
- Culture management
- Shared localization services
- Validation localization
- API localization
- Identity localization
- Email localization
- Testing

Excluded:

- Product translations
- Category translations
- Brand translations
- Attribute translations
- Search localization
- SEO localization
- Admin translation management

Those belong to Phase 2 (Catalog).

---

# SubPhase 1.6.1 — Localization Architecture

## Goal

Define the architectural foundation for localization across the entire platform.

## Tasks

- Define supported cultures.
- Define default culture.
- Define UI culture strategy.
- Define RTL/LTR strategy.
- Define localization folder structure.
- Define resource naming conventions.
- Define localization conventions for all future modules.

## Deliverables

- Localization architecture documented.
- Supported culture strategy established.
- Resource organization defined.

---

# SubPhase 1.6.2 — ASP.NET Core Localization Infrastructure

## Goal

Configure the application localization pipeline.

## Tasks

- Configure RequestLocalizationMiddleware.
- Configure LocalizationOptions.
- Configure supported cultures.
- Configure supported UI cultures.
- Configure default request culture.
- Configure Accept-Language header support.
- Configure CultureInfo resolution.

## Acceptance Criteria

- Requests automatically resolve the current culture.
- Culture changes based on Accept-Language header.
- Default culture is applied when no language is provided.

---

# SubPhase 1.6.3 — Shared Resource System

## Goal

Create the shared localization resources.

## Tasks

- Create Resources folder.
- Create shared resource files.
- Define shared localization keys.
- Organize resources for future expansion.

## Typical Resource Categories

- Validation
- Authentication
- Authorization
- Common messages
- Errors
- Success messages

## Acceptance Criteria

- Shared resources available.
- Localization keys resolved correctly.
- Resources organized consistently.

---

# SubPhase 1.6.4 — Localization Abstractions

## Goal

Provide framework-independent localization abstractions.

## Tasks

Create shared abstractions for:

- Current Culture
- Current UI Culture
- Localization service
- Resource access

These abstractions should be available to all modules without exposing ASP.NET Core implementation details.

## Acceptance Criteria

- Business modules depend on abstractions only.
- Localization implementation remains replaceable.

---

# SubPhase 1.6.5 — Validation Localization

## Goal

Localize validation messages.

## Tasks

- Connect FluentValidation to localization resources.
- Replace hardcoded validation messages.
- Resolve validation messages through resource keys.

## Acceptance Criteria

- Validation messages change according to request culture.
- No hardcoded validation messages remain.

---

# SubPhase 1.6.6 — ProblemDetails Localization

## Goal

Localize API error responses.

## Tasks

Localize:

- Validation errors
- Authentication errors
- Authorization errors
- Business errors
- Not Found responses
- Conflict responses
- Internal server messages where applicable

## Acceptance Criteria

- API responses return localized messages.
- Error format remains unchanged.
- Only displayed text changes according to culture.

---

# SubPhase 1.6.7 — Identity Localization

## Goal

Localize Identity-related functionality.

## Tasks

Localize messages for:

- Login
- Register
- Email Verification
- Password Reset
- Change Password
- Account Lockout
- Invalid Credentials
- Security messages

## Acceptance Criteria

- Identity messages are localized.
- No hardcoded Identity messages remain.

---

# SubPhase 1.6.8 — Email Localization

## Goal

Prepare multilingual email templates.

## Tasks

Create localized templates for:

- Email Verification
- Password Reset
- Welcome Email
- Security Notifications

## Acceptance Criteria

- Email templates exist for every supported language.
- Email content is selected based on current culture.

---

# SubPhase 1.6.9 — API Documentation Localization Support

## Goal

Prepare the API for localization testing.

## Tasks

- Add Accept-Language header to Swagger.
- Document supported cultures.
- Verify localized API behavior through Swagger.

## Acceptance Criteria

- Swagger allows selecting request language.
- Localized responses can be tested directly.

---

# SubPhase 1.6.10 — Testing & Verification

## Goal

Verify the localization infrastructure.

## Test Areas

### Culture Resolution

Verify:

- Default culture
- Accept-Language handling
- Unsupported cultures
- Culture fallback

### Validation

Verify:

- Validation messages
- Localized resource resolution

### API Errors

Verify:

- ProblemDetails localization
- Authentication errors
- Authorization errors
- Business errors

### Identity

Verify:

- Login
- Register
- Password reset
- Email verification

### Email Templates

Verify:

- Correct localized template selection
- Correct localized content rendering

---

# Completion Criteria

Phase 1.6 is complete when:

- Localization infrastructure is configured.
- Multiple cultures are supported.
- Shared resource system is operational.
- Localization abstractions are available.
- Validation messages are localized.
- ProblemDetails responses are localized.
- Identity messages are localized.
- Email templates support multiple languages.
- Swagger supports localization testing.
- All localization tests pass successfully.

---

# Out of Scope

The following items are intentionally excluded from this phase:

- ProductTranslation
- CategoryTranslation
- BrandTranslation
- AttributeTranslation
- AttributeValueTranslation
- Search localization
- SEO localization
- Translation management
- Multilingual business entities

These items will be implemented during **Phase 2 – Catalog**.
