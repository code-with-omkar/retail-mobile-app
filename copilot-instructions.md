# QuickCommerce Project — Copilot Engineering Rules

## 1. PURPOSE
This is an existing QuickCommerce / Grocery Retail application.

The application must be evolved incrementally into a production-ready, scalable, maintainable platform.

The primary objective is:

> EXTEND the existing application safely. DO NOT unnecessarily rewrite it.
Existing working functionality must be preserved unless the requested task explicitly requires changing it.

These rules apply to EVERY Copilot task performed in this repository.

---

# 2. GOLDEN RULE
Before changing code:

1. Inspect the existing implementation.
2. Understand the current architecture.
3. Identify the files/classes affected.
4. Reuse existing functionality where possible.
5. Determine whether the requested change can be implemented without restructuring existing code.
6. Make the smallest safe change.
7. Verify that existing functionality is not broken.
Never assume a rewrite is better than an incremental change.

---

# 3. DO NOT MAKE UNREQUESTED CHANGES
Copilot MUST NOT make unrelated changes.

Do NOT:

- Refactor unrelated code.
- Rename existing classes without a requirement.
- Rename database tables/columns without a requirement.
- Move projects unnecessarily.
- Change namespaces unnecessarily.
- Replace existing frameworks unnecessarily.
- Replace working libraries without a requirement.
- Change UI styling unrelated to the requested feature.
- Change API contracts unnecessarily.
- Change database structure unnecessarily.
- Remove existing functionality.
- Remove existing code simply because another approach is preferred.
- Introduce new infrastructure without a clear requirement.
If an improvement is discovered outside the requested task:

DO NOT implement it automatically.

Report it as a recommendation instead.

---

# 4. ARCHITECTURE RULES

## 4.1 Architecture Principle
Prefer a:

> Modular Monolith + Clean Architecture / Layered Architecture
unless the existing application already has a different valid architecture that can be safely evolved.

Do NOT convert the application into microservices unless explicitly requested.

---

## 4.2 Separation of Responsibilities
Maintain clear separation between

Presentation/API
Application/Business Logic
Domain
Infrastructure/Data Access

Controllers should not contain complex business logic.

Business rules should not be implemented directly inside UI components.

Database access should not be scattered throughout controllers.

---

## 4.3 Dependency Direction
Preferred dependency direction:

API
↓
Application
↓
Domain

Infrastructure
↓
Application / Domain

Domain must remain independent of infrastructure-specific implementations wherever practical.

---

## 4.4 Reuse Before Create
Before creating:

- Service
- Repository
- Helper
- Component
- DTO
- Validator
- Utility
search the existing project for equivalent functionality.

If an existing implementation can be safely reused, extend it instead of creating duplicate functionality.

Avoid duplicate business logic.

---

# 5. MODULE BOUNDARIES
The target business modules are:

- Identity
- Customer
- Catalog
- Organization
- Store
- Inventory
- Pricing
- Cart
- Order
- Fulfillment
- Delivery
- Notification
- Administration

These are logical business boundaries.

Do not create separate projects for every module unless there is a strong architectural reason.

The application should remain maintainable for a small development team.

---

# 6. MULTI-ORGANIZATION / CLIENT RULE
The future platform may support multiple retail organizations.

Conceptually:

Platform
|
+-- Organization A
| |
| +-- Store A1
| +-- Store A2
|
+-- Organization B
|
+-- Store B1

Design new functionality so that it can support organization/store ownership where appropriate.

However:

DO NOT add OrganizationId to every table automatically.

Only introduce organization ownership where it has a valid business meaning.

---

# 7. CATALOG RULES
The catalog must remain category-agnostic.

The system currently focuses on grocery retail but must be able to support future categories such as:

- Vegetables
- Fruits
- Dairy
- Bakery
- Frozen Food
- Household
- Personal Care
- Other retail categories

Do NOT create category-specific database columns unless explicitly required.

Prefer:

Category
Product
ProductVariant
UnitOfMeasure
AttributeDefinition
CategoryAttribute
ProductAttributeValue

where appropriate.

---

# 8. PRODUCT VARIANT RULE
Do not assume one Product equals one sellable item.

The architecture should be able to support:

Example:

Coke
├── 250 ml
├── 500 ml
└── 1 L

Tomato
├── 500 g
└── 1 kg

Where appropriate, inventory and pricing should operate at the sellable variant level.

Do not introduce variants into unrelated existing functionality unless required by the current task.

---

# 9. DATABASE RULES

## 9.1 Database Is Protected
The database is a critical part of the application.

Never modify database schema casually.

Before changing database structure:

1. Inspect existing tables.
2. Inspect relationships.
3. Inspect indexes.
4. Inspect existing data dependencies.
5. Inspect EF migrations/scripts.
6. Determine backward compatibility.
7. Identify impact on existing APIs.
8. Identify impact on existing UI.

---

## 9.2 Existing Data Must Be Preserved
Never:

- Drop tables casually.
- Drop columns casually.
- Delete existing data.
- Rename columns casually.
- Rename tables casually.
- Recreate the entire database for convenience.

If a schema change is required, prefer additive changes.

Example:

Preferred:

ADD new column
ADD new table
ADD new relationship
Migrate data gradually

Avoid:

DROP + RECREATE

unless explicitly approved.

---

# 10. DATABASE MIGRATION RULE
Every schema change must be:

- Explicit
- Reviewable
- Reversible where practical
- Backward compatible where possible

Do not automatically execute production database migrations.

Do not assume migrations should run automatically during application startup.

---

# 11. DATABASE PERFORMANCE RULES
Before adding queries:

- Check existing indexes.
- Avoid unnecessary SELECT *.
- Avoid N+1 queries.
- Use appropriate projections.
- Use pagination for large datasets.
- Avoid loading entire tables into memory.
- Respect existing transaction boundaries.

For large tables, consider indexing and query execution impact before implementation.

---

# 12. CONCURRENCY / INVENTORY RULE
Inventory is business-critical.

Inventory updates must consider concurrent orders.

Never implement:

Read stock
→ subtract in memory
→ save

without considering concurrent requests.

Inventory operations should use appropriate transactional/concurrency mechanisms.

Inventory reservation should be considered when implementing checkout/order flows.

---

# 13. PRICE SNAPSHOT RULE
Orders must preserve historical purchase information.

Do not depend on the current Product price when displaying historical orders.

Order items should retain the price/value applicable at the time of purchase.

Future price changes must not change historical orders.

---

# 14. AUTHORIZATION RULES
Never rely only on frontend authorization.

Backend authorization is mandatory.

Do not implement authorization using scattered hardcoded checks such as:

if role == "Admin"

unless there is a specific reason.

Prefer:

Role
Permission
Scope

where appropriate.

Potential roles include:

PlatformAdmin
OrganizationAdmin
StoreManager
StoreStaff
Customer

These roles should not be assumed to be final until implemented according to the application's actual authorization design.

---

# 15. DATA SCOPE RULE
Never trust organization/store IDs supplied by the client as proof of authorization.

The authenticated user's organization/store scope must be validated on the server.

Example:

A StoreStaff user assigned to Store A must not access Store B merely by changing:

storeId=StoreB

in the request.

---

# 16. DELIVERY RULES
Delivery must remain configurable.

Do not hardcode delivery time directly into product/category code.

The architecture should be capable of supporting:

- Category delivery rules
- Store-specific overrides
- Organization-specific overrides
- Minimum delivery time
- Maximum delivery time
- Instant/ASAP delivery
- Scheduled delivery
- Delivery slots

Potential rule precedence:

Store + Category
↓
Organization + Category
↓
Platform + Category
↓
Default Rule

Do not implement all of these unless required by the current task.

---

# 17. UI / THEME RULES
The existing UI theme is PROTECTED.

Do not change:

- Primary colors
- Secondary colors
- Typography
- Font family
- Spacing system
- Border radius
- Button styling
- Form styling
- Table styling
- Navigation styling
- Layout conventions
- Material/Tailwind theme configuration

unless explicitly requested.

---

# 18. UI COMPONENT REUSE
Before creating a new UI component:

1. Search for an existing reusable component.
2. Check shared/common components.
3. Extend the existing component if appropriate.

Do not create duplicate:

- Buttons
- Inputs
- Selects
- Tables
- Date pickers
- Dialogs
- Search controls
- Form controls

when reusable components already exist.

---

# 19. FRONTEND ARCHITECTURE
Maintain:

- Feature-based organization
- Shared components
- Reusable services
- Route guards
- Authentication handling
- Authorization handling
- API abstraction
- Proper state management

Do not introduce unnecessary state-management libraries.

Use the existing project approach unless there is a clear requirement to change it.

---

# 20. API RULES
Maintain RESTful API design.

Do not unnecessarily break existing API contracts.

When an API contract must change:

1. Identify consumers.
2. Check frontend usage.
3. Check other integrations.
4. Prefer backward-compatible changes.
5. Consider API versioning where appropriate.

---

# 21. VALIDATION RULES
Validation should exist at the appropriate boundaries.

Use:

- Client-side validation for user experience.
- Server-side validation for security and correctness.
- Business validation inside the application/business layer.

Never rely only on frontend validation.

---

# 22. SECURITY RULES
Never:

- Store plain-text passwords.
- Expose secrets in source code.
- Hardcode connection strings containing credentials.
- Trust client-provided authorization claims.
- Return sensitive internal exceptions to clients.
- Disable security features just to make development easier.

Follow secure authentication and authorization practices.

---

# 23. CONFIGURATION RULES
Do not hardcode environment-specific values.

Use configuration for:

- Connection strings
- API URLs
- Feature flags
- Delivery configuration
- External service settings
- Environment-specific settings

Do not put production secrets into source control.

---

# 24. LOGGING AND ERROR HANDLING
Use the existing logging framework where available.

Do not introduce multiple logging frameworks unnecessarily.

Errors should:

- Be logged appropriately.
- Return safe client-facing messages.
- Avoid exposing sensitive implementation details.

Do not swallow exceptions silently.

---

# 25. TESTING RULES
New business-critical functionality should include appropriate automated tests.

Prioritize tests for:

- Business rules
- Pricing
- Inventory
- Delivery calculation
- Authorization
- Order processing

Do not modify existing tests simply to make a failing implementation pass.

If existing behavior changes intentionally, update the tests only after confirming the intended behavior.

---

# 26. PACKAGE / TECHNOLOGY RULE
Do not introduce a new NuGet/NPM/package dependency without evaluating whether an existing dependency already provides the required functionality.

Avoid unnecessary technology proliferation.

Do not introduce:

- Kafka
- RabbitMQ
- Redis
- Elasticsearch
- Kubernetes
- Microservices

unless the task explicitly requires it or there is a demonstrated technical need.

---

# 27. CHANGE SIZE RULE
Prefer small, focused changes.

A task should ideally change only the files required for that task.

Avoid large-scale refactoring during feature implementation.

If a refactoring is necessary:

1. Explain why.
2. Identify affected files.
3. Separate refactoring from feature changes where practical.

---

# 28. BEFORE MODIFYING FILES
Before making changes, provide a short plan containing:

1. What will change
2. Why it needs to change
3. Files likely to change
4. Database changes, if any
5. API changes, if any
6. UI changes, if any
7. Risks
8. Existing functionality that could be affected

Then implement only the approved scope.

---

# 29. AFTER MODIFYING FILES
After implementation:

1. Summarize changed files.
2. Explain database changes.
3. Explain API changes.
4. Explain UI changes.
5. Explain tests added/updated.
6. Identify anything that could not be verified.
7. Identify any recommended future improvements separately.

Do not hide unrelated modifications.

---

# 30. STOP CONDITIONS
STOP and ask for confirmation if the requested change would require:

- Dropping existing tables
- Deleting production data
- Breaking an existing API contract
- Changing authentication architecture
- Changing the global UI theme
- Replacing the database technology
- Replacing the frontend framework
- Converting the application to microservices
- Major project restructuring
- Removing major existing functionality
- Large-scale refactoring

Do not make such changes automatically.

---

# 31. PRIORITY ORDER
When requirements conflict, use this priority:

1. Security
2. Data integrity
3. Existing working functionality
4. Business requirements
5. Architecture consistency
6. Maintainability
7. Performance
8. UI consistency
9. Developer convenience

---

# 32. FINAL PRINCIPLE
The application is being EVOLVED, not REBUILT.

Always prefer:

UNDERSTAND
↓
PLAN
↓
MINIMAL CHANGE
↓
TEST
↓
VERIFY
↓
NEXT CHANGE

Never:

REWRITE
↓
HOPE IT WORKS

---

# 33. APPROVED TECHNOLOGY STANDARDS

These technology choices are the approved baseline for this project. Follow them unless explicitly instructed otherwise.

## 33.1 Frontend

The frontend must use the latest stable Angular LTS version appropriate for the project at the time of implementation.

Do NOT:

- Introduce React.
- Introduce Vue.
- Replace Angular with another frontend framework.

Follow the existing Angular architecture and upgrade incrementally where required.

Prefer:

- Standalone Angular components where appropriate.
- Feature-based organization.
- Reusable shared components.
- Angular routing.
- Route guards.
- Reactive forms.
- Proper API service abstraction.
- Angular Signals where appropriate.
- RxJS where appropriate.
- The existing UI component and theme system.

Do not introduce unnecessary frontend libraries.

## 33.2 Backend

Use ASP.NET Core and the current supported .NET LTS version selected for the project.

Use C# as the primary language.

Do NOT target preview or non-LTS .NET versions unless explicitly approved.

Do not migrate the backend to another framework. Maintain the existing backend architecture and improve it incrementally.

## 33.3 Database

Microsoft SQL Server is the approved relational database.

Do NOT replace SQL Server with PostgreSQL, MySQL, MongoDB, Cosmos DB, or another database unless explicitly approved.

Use Entity Framework Core where already applicable, SQL queries or stored procedures where appropriate, proper indexing, transactions, constraints, foreign keys, concurrency controls, pagination, and query optimization.

All database changes must follow the database protection and migration rules above.

## 33.4 Hosting

Microsoft IIS is the primary application hosting platform. Host ASP.NET Core applications on IIS where applicable.

Do not introduce Kubernetes, containers, or another hosting platform as a replacement for IIS unless explicitly approved.

## 33.5 Load Balancing and Shared State

The production architecture must support multiple application/API instances behind a load balancer, such as multiple IIS servers sharing SQL Server.

Do not depend on local server memory or local server state in a way that prevents requests from being handled by another instance.

Where shared state is required, design it appropriately. Do not implement load balancing inside application business logic.

## 33.6 Availability and Deployment

The production design should support near-zero-downtime deployments through multiple IIS instances behind a load balancer and rolling or blue-green deployment patterns where practical.

Do not intentionally take the entire application offline when the architecture can avoid it. Do not claim absolute zero downtime.

Database changes should be backward compatible wherever practical.

The target is near-zero-downtime deployment and high availability.

## 33.7 Source Control

Use Git-compatible source control for all changes.

Do not create unrelated generated files or environment-specific files in source control. Never commit sensitive configuration or secrets.

## 33.8 CI/CD

The project must support Git-based CI/CD. The eventual pipeline should support:

- Restoring dependencies.
- Building the frontend and backend.
- Running unit tests.
- Quality and configuration validation.
- Packaging artifacts.
- Deployment to IIS.
- Health checks and deployment verification.

Do not implement the complete CI/CD pipeline unless explicitly requested.

## 33.9 Technology Change Policy

Do not change an approved technology automatically.

If another technology appears preferable:

1. Do not replace the approved technology.
2. Explain the reason.
3. Explain the benefit.
4. Explain the migration impact.
5. Wait for explicit approval.

Approved baseline:

- Frontend: Angular LTS.
- Backend: ASP.NET Core / .NET LTS.
- Database: SQL Server.
- Hosting: IIS.
- Load balancing: Multiple IIS instances behind a load balancer.
- Source control: Git.
- CI/CD: Git-based CI/CD.
- Architecture: Modular Monolith with Clean or Layered Architecture.
- Deployment: Near-zero downtime.

---

# 34. PHASE 1C APPLICATION-LAYER RULES

These checks are mandatory for every Phase 1C task and must be performed every time. Phase 1B is approved and must be preserved.

## 34.1 Fixed Scope and Technology

Preserve:

- Angular 20 LTS.
- ASP.NET Core / .NET 10 LTS.
- C#.
- SQL Server.
- Entity Framework Core.
- IIS as the hosting target.
- Modular Monolith architecture.
- Clean or Layered backend architecture.
- Existing Git and future CI/CD direction.

Do NOT introduce microservices or change the approved technologies.

Phase 1C must not implement:

- JWT, authentication, authorization, roles, or permissions.
- Redis, Kafka, RabbitMQ, SignalR, payments, or delivery integration.
- IIS, load balancing, Blue/Green deployment, CI/CD, Docker, Azure deployment, or production migration automation.
- Angular restructuring, Angular theme changes, or any Flutter changes.
- New business modules or unnecessary database redesign.

## 34.2 Phase 1B Protection

Do not break or remove:

- SQL Server connection behavior.
- `QuickCommerceDbContext`.
- EF Core mappings.
- The `InitialCreate` migration.
- Deterministic seed data.
- `StoreInventory.RowVersion` and optimistic concurrency.
- Transactional order persistence.
- Existing persistence interfaces.
- Existing API routes and response behavior.

Never hardcode database credentials or commit passwords.

## 34.3 Mandatory Inspection Before Changes

Before modifying files, inspect the current:

1. Solution and project references.
2. `Program.cs` service registrations and middleware pipeline.
3. `OrdersController` and all other affected controllers.
4. Application services, DTOs, persistence abstractions, and validators.
5. Infrastructure registration and EF Core implementation.
6. Exception and error handling.
7. Existing tests and test configuration.
8. Angular and Flutter status to confirm they remain untouched.

Do not edit during inspection. After inspection, provide a concise plan covering:

- What moves out of `Program.cs`.
- Application services/use cases.
- Validators.
- DTOs.
- Exception handling.
- Tests to add or update.

## 34.4 Application Layer

Move business and use-case logic out of API controllers.

Application services are responsible for:

- Use-case orchestration.
- Business workflow coordination.
- Application DTOs.
- Request and response contracts.
- FluentValidation validators.
- Persistence abstractions.

Application MUST NOT reference Infrastructure, EF Core, `DbContext`, SQL Server, or Angular.

Use focused interfaces only where they provide meaningful value. Do NOT add generic service, repository, CRUD, unit-of-work, CQRS, or event-sourcing abstractions without a concrete need.

## 34.5 Controllers and DTO Boundaries

Controllers may only:

1. Receive and bind HTTP requests.
2. Invoke application services/use cases.
3. Map application results to HTTP responses.
4. Return appropriate status codes.

Controllers MUST NOT:

- Access `DbContext`, EF Core, repositories, or persistence implementations.
- Directly access `InMemoryCommerceStore`.
- Manipulate inventory.
- Calculate business decisions.
- Implement transaction logic.
- Implement application validation or domain workflows.

Do not expose EF Core or Domain entities directly where DTO boundaries are required. Preserve the existing API envelope, routes, request behavior, and response compatibility wherever practical.

## 34.6 FluentValidation

Use FluentValidation in Application for appropriate requests, especially order creation.

Validate at least:

- Valid `UserId`.
- Non-empty item collection.
- Valid `ProductId` values.
- Quantity greater than zero.
- Non-empty delivery address.
- Valid latitude and longitude ranges.

Keep validation separate from business rules. Request validation must not replace inventory availability, pricing, store selection, transaction, or concurrency rules.

Validators MUST NOT be placed in Infrastructure or controllers.

## 34.7 Program.cs and Registration

`Program.cs` must remain small and contain only composition/bootstrap concerns:

1. Create the builder.
2. Call grouped API, Application, and Infrastructure registration extensions.
3. Build the application.
4. Call grouped API pipeline configuration.
5. Run the application.

Move registrations into idiomatic extension methods in the correct projects:

- Application services and validators in Application.
- EF Core, persistence implementations, and infrastructure services in Infrastructure.
- Controllers, Swagger/OpenAPI, CORS, and API behavior in Api.

Do not leave a long list of individual registrations, business logic, database implementation logic, or validator registration in `Program.cs`.

## 34.8 Pipeline and Error Handling

Keep API middleware and pipeline configuration grouped outside `Program.cs` where appropriate.

If centralized exception handling is absent, add only a minimal exception handler or middleware. Translate application/business exceptions into consistent HTTP responses.

Do not expose database errors, connection strings, credentials, stack traces, or internal implementation details to clients.

## 34.9 Dependency Guardrails

Verify every time:

- Domain has no project dependencies and no Application, Infrastructure, or EF Core references.
- Application references Domain and framework-neutral dependencies only.
- Application has no Infrastructure or EF Core references.
- Infrastructure references Application and Domain and owns EF Core.
- API uses Application abstractions and only references Infrastructure for composition/configuration.
- Controllers do not directly use `DbContext` or Infrastructure implementations.
- No circular project dependencies exist.

## 34.10 Required Testing

Before completion, add or update focused tests without modifying tests merely to make them pass:

- Valid order creation.
- Invalid order request validation.
- Quantity, product, address, and coordinate validation.
- Order price snapshot behavior.
- Inventory conflict behavior.
- Existing API/application behavior.

Do not require SQL credentials for ordinary unit tests. SQL integration tests must use the existing environment-variable approach when applicable.

## 34.11 Mandatory Validation Every Time

Run:

```text
dotnet build QuickCommerce.slnx
dotnet test QuickCommerce.slnx
```

Also verify:

- Existing SQL Server and EF Core behavior remains intact.
- Existing migrations are not removed or replaced unnecessarily.
- Existing API routes remain available.
- Angular source and theme are untouched.
- Flutter source is untouched.
- No passwords, secrets, real credentialed connection strings, or temporary SQL scripts were added.
- No unrelated generated files or unrelated changes were introduced.
- Git status and diff are reviewed.

## 34.12 Required Implementation Discipline

Follow this exact sequence every time:

```text
INSPECT
↓
PLAN
↓
IMPLEMENT ONLY APPROVED PHASE 1C SCOPE
↓
BUILD
↓
TEST
↓
VERIFY ARCHITECTURE, SECRETS, GIT, ANGULAR, AND FLUTTER
↓
REPORT
```

Do not proceed automatically to Phase 1D or any later phase.

## 34.13 Required Final Report

Every Phase 1C completion report must include:

1. Files added.
2. Files modified.
3. Files deleted, if any.
4. `Program.cs` responsibility before and after.
5. Application services/use cases introduced or changed.
6. Validators introduced or changed.
7. DTOs introduced or changed.
8. Exception-handling changes.
9. Build and test results.
10. Architecture verification.
11. Secret/Git review.
12. Warnings, unresolved issues, and remaining gaps.

End with exactly one of:

```text
PHASE 1C READY FOR REVIEW
```

or

```text
PHASE 1C NOT READY — FIX REQUIRED
```
