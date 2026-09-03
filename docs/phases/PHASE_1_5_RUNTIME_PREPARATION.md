# PHASE 1.5 — Runtime Preparation & First Startup

> **Status:** Implemented & Verified
> **Prerequisites:** Phase 0 ✅ Completed, Phase 1 ✅ Completed
> **Completion report:** `docs/completed/PHASE_1_5_COMPLETION_REPORT.md`
> **Goal:** Prepare the solution for its first real execution by configuring the runtime environment, generating the initial database schema, enabling developer tooling, and validating that the Identity module works end-to-end.

---

# Overview

Phase 1 delivered the complete **Identity & Access** capability from an architectural and business perspective.

The Domain, Application, Infrastructure, API, and automated tests are implemented.

However, before moving to **Phase 2 (Catalog)**, the platform must be transformed from a codebase into a runnable application.

This phase focuses exclusively on **runtime readiness**.

No new business functionality should be introduced.

No new modules should be implemented.

The objective is to ensure that the current implementation can be executed, debugged, verified, and used as a stable foundation for the remaining development phases.

---

# Objectives

At the end of this phase the project should:

- Build successfully.
- Start successfully.
- Connect to SQL Server.
- Create the Identity database schema.
- Expose Swagger UI.
- Support JWT authentication through Swagger.
- Allow manual testing of all Identity endpoints.
- Successfully complete the first end-to-end execution.

---

# Scope

This phase covers only runtime preparation.

Included:

- Runtime configuration
- Application hosting
- Swagger / OpenAPI
- Database initialization
- Entity Framework migrations
- Authentication configuration verification
- Authorization verification
- Startup validation
- Manual API verification
- Runtime stabilization

Not included:

- New business features
- Catalog module
- Orders
- Payments
- Inventory
- Email provider implementation
- API Versioning
- Rate limiting
- CI/CD
- Production deployment

---

# Architecture Principles

This phase must not modify the project architecture.

It must preserve:

- Modular Monolith
- Clean Architecture
- Module isolation
- Module-per-DbContext
- BuildingBlocks reuse

No architectural refactoring should occur.

Only runtime readiness.

---

# Deliverables

The following deliverables must exist after completing this phase:

- Swagger UI available
- OpenAPI document generated
- SQL Server database created
- Initial EF Core migration generated
- Identity schema verified
- JWT authentication working
- Authorization verified
- Manual API verification completed
- Runtime issues resolved
- Runtime completion report

---

# Execution Plan

---

# Step 1 — Runtime Configuration Audit

## Goal

Verify that the application hosting configuration is complete.

## Review

- Program.cs
- Service registration
- Dependency Injection
- Module registration
- Middleware pipeline
- Configuration binding
- Authentication registration
- Authorization registration
- Health Checks
- Logging
- Observability

## Acceptance Criteria

- Application builds.
- Dependency Injection resolves correctly.
- No startup exceptions.
- All required services registered.

---

# Step 2 — Swagger & Developer Experience

## Goal

Expose the HTTP API for development and testing.

## Tasks

Configure:

- Swagger
- OpenAPI
- XML documentation
- JWT Authorization support
- Security definition
- Security requirements

Swagger should expose every Identity endpoint.

The developer should be able to authorize directly from Swagger.

## Acceptance Criteria

- Swagger UI available.
- OpenAPI generated.
- JWT authorization supported.
- All Identity endpoints visible.

---

# Step 3 — Database Initialization

## Goal

Create the first persistent database schema.

## Tasks

Review:

- DbContext
- Entity Configurations
- Value Converters
- Relationships
- Constraints
- Indexes

Generate:

InitialIdentity migration.

Apply migration to SQL Server.

Verify:

- Tables
- Foreign Keys
- Indexes
- Constraints

## Acceptance Criteria

- Migration generated successfully.
- Database created.
- Identity schema verified.

---

# Step 4 — First Application Startup

## Goal

Run the application for the first time.

## Verify

- Build
- Startup
- SQL Connection
- Authentication initialization
- Authorization initialization
- Health Checks
- Logging
- Exception Handling

## Acceptance Criteria

- Application starts successfully.
- No startup exceptions.
- Health endpoints operational.

---

# Step 5 — Manual Identity Verification

## Goal

Verify all Identity workflows manually.

## Required Flow

Register

↓

Verify Email

↓

Login

↓

Authorize (Swagger)

↓

Get Current User

↓

Refresh Token

↓

Logout

↓

Attempt Refresh Again

Expected Result:

Refresh should fail after logout.

---

## Password Flow

Forgot Password

↓

Reset Password

↓

Login With New Password

---

## Authorization Flow

Customer Token

↓

Protected Endpoint

↓

Authorized

Admin Endpoint

↓

Forbidden

---

## Acceptance Criteria

Every Identity endpoint behaves as expected.

---

# Step 6 — Runtime Stabilization

## Goal

Resolve all runtime-related issues discovered during execution.

Possible fixes include:

- Dependency Injection issues
- Configuration issues
- Migration issues
- SQL issues
- Authentication issues
- Authorization issues
- Swagger issues
- Serialization issues
- Pipeline issues

No architectural refactoring is allowed.

No new functionality may be introduced.

---

# Deferred Work

The following items are intentionally postponed to later phases:

- SMTP / Email Provider
- API Versioning
- Rate Limiting
- Refresh Token Hashing
- Refresh Token Revocation after Password Change
- Access Token Revocation (jti / Token Version)
- CI/CD Pipeline
- Production Secret Management
- OAuth / External Login
- MFA
- Passkeys

These items do not block development of Phase 2.

---

# Exit Criteria

This phase is complete when all of the following are true:

- Solution builds successfully.
- Application starts successfully.
- Swagger UI is available.
- JWT authentication works.
- Authorization works.
- Initial migration exists.
- SQL Server schema created.
- Identity database verified.
- All Identity endpoints tested manually.
- No runtime exceptions remain.
- Runtime completion report documented.

---

# Next Phase

After successful completion of this phase, the project is ready to begin:

**Phase 2 — Catalog Module**

At that point the platform will have:

- Stable Architecture
- Verified Runtime
- Operational Identity Module
- Valid Database
- Verified API
- Reliable Development Environment

This provides a solid foundation for implementing the remaining business modules.
