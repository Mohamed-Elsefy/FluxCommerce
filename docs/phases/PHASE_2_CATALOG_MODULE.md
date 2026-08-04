# Phase 2 — Catalog Module

> **Phase Goal**
>
> Build the first Core Commerce module of the platform: **Catalog**.
>
> This phase introduces the first production business domain in the system and establishes the foundation upon which future modules (Cart, Pricing, Orders, Inventory, Search, etc.) will depend.
>
> The Catalog module owns all product-related business state and must strictly follow the architectural principles defined in:
>
> - `PROJECT_CONTEXT.md`
> - `ARCHITECTURE.md`
>
> In particular:
>
> - Modular Monolith
> - Clean Architecture
> - Domain-Driven Design
> - Module-per-DbContext
> - Module Ownership
> - Explicit Contracts
> - No Cross-Module Persistence
> - No Cross-Module Business Logic

---

# Objectives

At the end of Phase 2, the project should provide a complete Catalog module capable of managing products and exposing them to the rest of the platform while maintaining strict module boundaries.

The Catalog module is responsible for:

- Categories
- Products
- Product Variants
- SKU Management
- Product Images
- Product Attributes
- Product Specifications
- Product Metadata
- Product Lifecycle
- Product Visibility
- SQL-based Search
- Filtering
- Sorting
- Pagination

The Catalog module is **not responsible** for:

- Inventory
- Stock
- Reservations
- Pricing Rules
- Promotions
- Coupons
- Orders
- Payments
- Reviews
- Wishlist
- Shipping

Those capabilities belong to future modules.

---

# Architectural Principles

During Phase 2 the following architectural rules remain mandatory.

- Catalog owns its own business state.
- Catalog owns its own DbContext.
- No module may access Catalog internals directly.
- Catalog exposes explicit contracts only.
- No cross-module foreign keys.
- No cross-module EF relationships.
- Business rules belong inside the Domain.
- Controllers remain thin.
- Application orchestrates use cases.
- Infrastructure implements abstractions.
- BuildingBlocks contains only cross-cutting concerns.

---

# Phase 2.1 — Catalog Foundation & Domain Model

## Goal

Establish the Catalog module and implement its core Domain Model.

## Scope

### Module Structure

Create:

- Catalog.Domain
- Catalog.Application
- Catalog.Infrastructure
- Catalog.Contracts

### Infrastructure

- CatalogDbContext
- Module Registration
- Dependency Injection
- Entity Configurations
- Initial Migration

### Domain

Create the initial Domain Model including:

- Product Aggregate Root
- Category Aggregate Root
- Core Value Objects
- Product Status
- Domain Errors
- Domain Events
- Business Invariants

### Persistence

- Repository Interfaces
- Repository Implementations

## Deliverable

A fully functional Catalog module foundation with no business APIs implemented yet.

---

# Phase 2.2 — Category Management

## Goal

Implement complete Category management.

## Scope

### Business Capabilities

- Create Category
- Update Category
- Soft Delete
- Restore
- Get Category
- List Categories

### Business Rules

- Parent Categories
- Category Hierarchy
- Unique Name
- Slug
- Active / Inactive Status

### Application

- Commands
- Queries
- Validators

### API

Category Management Endpoints

## Deliverable

Complete Category Management capability.

---

# Phase 2.3 — Product Management

## Goal

Implement core Product management.

## Scope

### Business Capabilities

- Create Product
- Update Product
- Activate
- Deactivate
- Archive
- Soft Delete
- Restore

### Product Metadata

- Name
- Description
- Slug
- SEO Metadata
- Product Status
- Product Visibility

### Business Rules

- Product Lifecycle
- SKU Validation
- Required Fields
- Domain Validation

### Application

- Commands
- Queries
- Validation

### API

Product Management Endpoints

## Deliverable

Core Product Management capability.

---

# Phase 2.4 — Product Variants

## Goal

Support products with multiple purchasable variants.

## Scope

### Variant Management

- Create Variant
- Update Variant
- Delete Variant

### Variant Capabilities

Examples:

- Size
- Color
- Material
- Capacity
- Style

### SKU

- SKU Assignment
- SKU Validation
- SKU Uniqueness

### Business Rules

- Variant Integrity
- Product-Variant Relationship

### API

Variant Management Endpoints

## Deliverable

Complete Product Variant capability.

---

# Phase 2.5 — Product Attributes & Specifications

## Goal

Support dynamic product information across different product categories.

## Scope

### Dynamic Attributes

Examples:

- RAM
- CPU
- Screen Size
- Storage
- Material
- Fabric
- Weight

### Specifications

Support descriptive product specifications.

### Category-specific Attributes

Different categories may define different attribute sets.

### Business Rules

- Validation
- Required Attributes
- Attribute Definitions
- Attribute Values

### Filtering Metadata

Prepare metadata required for future filtering capabilities.

## Deliverable

Dynamic Attribute & Specification system.

---

# Phase 2.6 — Product Images & Media

## Goal

Manage product media.

## Scope

### Image Management

- Upload Reference
- Primary Image
- Image Ordering
- Alt Text
- Metadata

### Architecture

The Catalog stores only image references and metadata.

Actual storage implementation remains an Infrastructure concern.

## Deliverable

Complete Product Media capability.

---

# Phase 2.7 — Catalog Query APIs

## Goal

Expose optimized read operations for Catalog consumers.

## Scope

### Queries

- Product Details
- Product List
- Category List

### Features

- SQL-based Search
- Filtering
- Sorting
- Pagination

### Read Models

Create optimized DTOs and projections.

### Explicitly Out of Scope

- Elasticsearch
- Full Text Search Engines
- Search Index Synchronization

These belong to future phases.

## Deliverable

Production-ready Catalog query APIs.

---

# Phase 2.8 — Catalog Hardening & Production Readiness

## Goal

Finalize the Catalog module for production readiness.

## Scope

### Validation

Review all validation rules.

### Security

Review authorization requirements.

### Localization

Verify localization coverage.

### Testing

- Unit Tests
- Integration Tests
- Architecture Tests

### Documentation

- Update Architecture Documentation
- Completion Report
- Migration Verification

### API

Swagger Review

### Performance

Review SQL queries and overall performance.

## Deliverable

Production-ready Catalog module.

---

# Expected Deliverables

At the completion of Phase 2, the project should include:

- Complete Catalog Module
- Catalog Domain Model
- Category Management
- Product Management
- Product Variants
- Dynamic Attributes
- Specifications
- Product Images
- CatalogDbContext
- EF Core Migrations
- REST API
- SQL Search
- Filtering
- Sorting
- Pagination
- Validation
- Localization
- Testing
- Documentation

---

# Out of Scope

The following capabilities are intentionally excluded from Phase 2.

- Inventory
- Stock
- Reservation
- Pricing Engine
- Promotions
- Coupons
- Cart
- Orders
- Payments
- Reviews
- Wishlist
- Shipping
- Elasticsearch
- RabbitMQ Business Events
- Saga
- Order Processing

These capabilities belong to later implementation phases.

---

# Phase Completion Criteria

Phase 2 is considered complete when:

- The Catalog module fully complies with the architecture defined in `ARCHITECTURE.md`.
- The module respects all modular boundaries.
- All planned sub-phases are completed.
- The Catalog exposes complete management and query capabilities.
- No business logic leaks into other modules.
- Documentation, testing, migrations, and localization are complete.
- The module is ready to serve as the foundation for future Commerce modules.
