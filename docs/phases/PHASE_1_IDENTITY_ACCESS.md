# Phase 1 — Identity & Access

## Status

Phase: 1

Name: Identity & Access

Status: Implemented & Verified

Completion report: `docs/completed/PHASE_1_COMPLETION_REPORT.md`

This document defines the complete implementation scope for Phase 1 of the Enterprise Commerce & Order Management Platform.

The AI implementation agent MUST read and understand the following documents before implementing this phase:

1. `docs/PROJECT_CONTEXT.md`
2. `docs/ARCHITECTURE.md`
3. `docs/phases/PHASE_0_ARCHITECTURE_FOUNDATION.md`
4. This document

`PROJECT_CONTEXT.md` remains the highest-level project specification.

If any conflict exists, PROJECT_CONTEXT.md takes precedence.

---

# 1. Phase Objective

Phase 1 establishes the Identity & Access Platform Capability.

Unlike the Commerce modules, Identity is responsible for platform-level authentication and authorization rather than commerce business logic.

The objective is to provide a secure, production-oriented identity system that supports future commerce modules without coupling them to identity implementation details.

At the end of this phase the platform must support:

- User Registration
- Authentication
- JWT Access Tokens
- Refresh Tokens
- Email Verification
- Password Reset
- Account Lockout
- Role-Based Authorization
- Current User abstraction

The implementation must follow the architecture established in Phase 0.

Identity must not violate module boundaries.

Other modules must never depend directly on Identity internals.

---

# 2. Architectural Position

Identity is a Platform Capability.

It is NOT a Commerce Bounded Context.

However, within the Modular Monolith it is implemented as an independent module using the same architectural template as every business module.

The module structure is:

Modules

    Identity
        Domain
        Application
        Infrastructure
        Contracts

Identity owns:

- Authentication
- Authorization
- User Accounts
- Security Tokens
- Roles

Identity does NOT own:

- Customer Profile
- Shopping Data
- Orders
- Payments
- Inventory

Commerce modules must depend only on platform abstractions such as:

- ICurrentUser
- UserId
- Claims

They must never depend directly on Identity entities or DbContext.

---

# 3. Scope of Phase 1

Phase 1 consists of the following implementation stages.

3.1 Identity Module Foundation

3.2 Identity Domain Model

3.3 Application Layer

3.4 Authentication Infrastructure

3.5 Registration & Authentication

3.6 Account Lifecycle

3.7 API & Authorization

3.8 Security Hardening

3.9 Testing & Verification

Each subphase builds on the previous one.

The AI implementation agent MUST complete them sequentially.

The implementation steps below (§4–§12) are numbered **Step 1–Step 9**. Note: the "Phase 1.5" and "Phase 1.6" labels are reserved for the standalone companion phases **Runtime Preparation** (`docs/phases/PHASE_1_5_RUNTIME_PREPARATION.md`) and **Internationalization** (`docs/completed/PHASE_1_COMPLETION_REPORT.md` §14); the internal steps of Phase 1 use Step numbering to avoid collision.

---

# 4. Step 1 — Identity Module Foundation

Objective

Create the Identity module foundation without implementing business behavior.

Implementation includes:

- Identity.Domain
- Identity.Application
- Identity.Infrastructure
- Identity.Contracts

Create:

IdentityDbContext

The Identity module owns its own persistence.

IdentityDbContext must inherit from BaseDbContext.

The module must provide:

- Dependency Injection registration
- Design-time DbContext Factory
- EF Core configuration
- SQL Server registration
- Connection string configuration
- Migrations history table

The module must not share DbContext with any other module.

The persistence model follows the Module-per-DbContext strategy defined in Phase 0.

---

# 5. Step 2 — Identity Domain Model

The Domain layer contains all business rules related to identity.

No infrastructure code belongs here.

The initial Aggregate Root is:

User

The User aggregate owns:

- Email
- Password Hash
- Roles
- Verification Status
- Lockout State
- Refresh Tokens

Additional Entities:

- RefreshToken
- EmailVerificationToken
- PasswordResetToken

Value Objects:

- Email
- LockoutState

The Domain layer must also define:

Domain Events

Including:

- UserRegistered
- EmailVerified
- PasswordChanged
- PasswordResetRequested
- RefreshTokenRevoked

Domain Errors

Examples:

- DuplicateEmail
- InvalidCredentials
- EmailNotVerified
- AccountLocked
- InvalidToken

Business invariants must be enforced inside the aggregate.

No Application layer validation may replace Domain invariants.

---

# 6. Step 3 — Application Layer

The Application layer coordinates identity use cases.

Business orchestration belongs here.

Implementation includes:

Commands

- RegisterCustomer
- RegisterAdmin
- Login
- RefreshToken
- Logout
- VerifyEmail
- ForgotPassword
- ResetPassword
- ChangePassword
- ResendVerification

Queries

- GetCurrentUser

Each command must include:

- Handler
- Validator
- Result mapping

Application contracts include:

- IUserRepository
- IRoleRepository
- ITokenRepository
- IPasswordHasher
- IJwtProvider
- IEmailVerificationService
- IPasswordResetService

The Application layer depends only on Domain and BuildingBlocks.

---

# 7. Step 4 — Authentication Infrastructure

Infrastructure provides implementations for the contracts defined by the Application layer.

Implementation includes:

Password Hashing

JWT Token generation

JWT Validation

Refresh Token generation

Refresh Token persistence

Repository implementations

CurrentUser implementation

Authentication configuration

JWT Options

Clock implementation

Infrastructure contains technical implementations only.

Business rules remain inside the Domain.

---

# 8. Step 5 — Registration & Authentication

Implement the following use cases:

Customer Registration

Administrator Registration

Login

Logout

Refresh Token

The Login flow must:

Validate email

Verify password

Verify account state

Verify lockout state

Verify email verification

Generate:

Access Token

Refresh Token

Refresh tokens must support rotation.

Old tokens must become invalid after successful rotation.

---

# 9. Step 6 — Account Lifecycle

Implement:

Email Verification

Password Reset

Change Password

Resend Verification

Lockout

Unlock

Last Login tracking

Verification tokens must expire.

Password reset tokens must expire.

Tokens must be single-use.

---

# 10. Step 7 — API & Authorization

Expose the Identity API.

Endpoints include:

POST /auth/register

POST /auth/login

POST /auth/refresh

POST /auth/logout

POST /auth/verify-email

POST /auth/resend-verification

POST /auth/forgot-password

POST /auth/reset-password

POST /auth/change-password

GET /auth/me

Authentication middleware must replace the placeholder introduced in Phase 0.

JWT Bearer authentication must be enabled.

Role-based authorization policies include:

Customer

Admin

SuperAdmin

ProblemDetails must be returned for all expected failures.

---

# 11. Step 8 — Security Hardening

The implementation must include:

Password Policy

Refresh Token Rotation

Refresh Token Revocation

Token Expiration

Clock Skew handling

Account Lockout

Replay protection where appropriate

Registration idempotency

Security logging

Sensitive information must never appear in logs.

Passwords must never be logged.

Tokens must never be logged.

---

# 12. Step 9 — Testing & Verification

Identity must include:

Architecture Tests

Unit Tests

Integration Tests

Unit Tests cover:

User Aggregate

Refresh Tokens

Verification Tokens

Password Reset

Lockout

Application Tests cover:

Registration

Login

Refresh

Logout

Verification

Integration Tests cover:

Register → Verify → Login

Refresh flow

Forgot Password flow

Protected endpoints

Architecture Tests must verify:

Identity isolation

No module dependency violations

No forbidden references

---

# 13. Security Requirements

Passwords must never be stored in plaintext.

Passwords must always be hashed.

Authentication must use JWT.

Refresh Tokens must be cryptographically secure.

Email verification tokens must expire.

Password reset tokens must expire.

Failed login attempts must be tracked.

Accounts must enter Lockout after repeated failures.

Authorization must always be claim-based.

---

# 14. Architecture Constraints

Identity owns:

Authentication

Authorization

Users

Refresh Tokens

Verification Tokens

Identity does NOT own:

Customer Profile

Orders

Inventory

Catalog

Payments

No commerce module may access IdentityDbContext.

No commerce module may modify User state.

Communication must happen through abstractions.

---

# 15. Explicitly Out of Scope

The following are intentionally excluded from Phase 1:

OAuth

OpenID Connect

Google Login

Facebook Login

Microsoft Login

GitHub Login

Multi-Factor Authentication

Passkeys

Biometric Authentication

Device Management

Session Management Dashboard

Permissions Management

Fine-grained Authorization

External Identity Providers

Single Sign-On

Identity Federation

These capabilities may be introduced in later phases.

---

# 16. Implementation Principles

Do not over-engineer.

Prefer standard ASP.NET Core authentication capabilities.

Do not introduce unnecessary abstractions.

Preserve module ownership.

Preserve dependency direction.

Controllers must remain thin.

Business rules belong in Domain.

Application orchestrates use cases.

Infrastructure provides technical implementations.

Every public API must return consistent ProblemDetails responses.

Security takes precedence over convenience.

---

# 17. Expected Deliverables

At the completion of Phase 1 the repository must contain:

- Complete Identity Module
- IdentityDbContext
- User Aggregate
- Refresh Token model
- Email Verification model
- Password Reset model
- Registration
- Login
- JWT Authentication
- Refresh Token Rotation
- Role-Based Authorization
- CurrentUser implementation
- Authentication Middleware
- Identity API
- Password Hashing
- Lockout Policy
- Email Verification
- Password Reset
- Architecture Tests
- Unit Tests
- Integration Tests
- Updated Documentation

Phase 1 completes the Identity & Access platform capability and prepares the platform for the implementation of the first Commerce module in Phase 2.
