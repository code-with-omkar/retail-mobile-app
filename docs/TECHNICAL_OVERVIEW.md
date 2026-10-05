# QuickCart Technical Overview

Reference notes for continuing development. Confirm package manifests and environment configuration before upgrading dependencies or deploying; versions and settings can change.

## System shape

QuickCart is implemented as a modular monolith. The customer app and admin portal are separate clients of one ASP.NET Core API. Backend responsibilities are split across:

| Project | Responsibility |
| --- | --- |
| `src/QuickCommerce.Api` | HTTP controllers, middleware, dependency registration, authentication/authorization pipeline, health endpoints, OpenAPI |
| `src/QuickCommerce.Application` | DTOs, service interfaces/implementations, business workflows, validators |
| `src/QuickCommerce.Domain` | Commerce, identity, organization, and store domain types |
| `src/QuickCommerce.Infrastructure` | EF Core persistence, SQL Server mappings/migrations, seed data, security services, caching, health checks |
| `src/QuickCommerce.Tests` | .NET tests for services, policies, validation, and persistence model |
| `admin-portal` | Angular operations/admin client |
| `mobile` | Flutter customer client prototype |

## Technology by client and service

### Mobile

- Flutter and Dart, using Material 3.
- Dependencies currently declared in `mobile/pubspec.yaml` are Flutter SDK, `cupertino_icons`, and development lints/tests.
- The current UI is implemented in `mobile/lib/main.dart` and uses local, in-memory sample product and cart-quantity data.
- Riverpod, Dio, go_router, and generated immutable/JSON DTO tooling are proposed in the mobile README, but are not currently declared dependencies.

### Admin portal

- Angular 20, TypeScript, RxJS, Angular Router, and Angular standalone components.
- Feature navigation is lazy-loaded from `admin-portal/src/app/app.routes.ts`.
- Authentication state, token storage, auth guards, and the auth interceptor are in `admin-portal/src/app/core`.
- API base URL and endpoint definitions are centralized in `admin-portal/src/app/core/config/api-config.ts`; configure the URL for the environment rather than assuming the local API address.
- Install and run from `admin-portal` using `npm install` and `npm run dev`. Check `package.json` for the available lint/build scripts.

### API and backend

- ASP.NET Core 10 (`net10.0`) and C#.
- Entity Framework Core 10 with the SQL Server provider.
- FluentValidation request validators live in the Application project. Controllers live in the API project.
- JWT bearer authentication and policy-based authorization are configured in the API service registration.
- Identity password hashing uses ASP.NET Core Identity's `IPasswordHasher<User>`.
- Optional distributed cache uses `Microsoft.Extensions.Caching.StackExchangeRedis`; when Redis is not configured, a no-op cache service is registered.
- Swagger/OpenAPI is enabled by the API pipeline in Development.

## Persistence and schema

- The EF Core context is `QuickCommerceDbContext` in `QuickCommerce.Infrastructure/Persistence`.
- Entity mappings are kept in `Persistence/Configurations`; model seed configuration is in `Persistence/SeedData.cs`.
- Migrations are in `Persistence/Migrations`. They cover the evolution of catalog/store/order data as well as cart, customers, notifications, identity/authorization, and store-staff approvals.
- SQL Server is the configured EF provider. Do not assume a database has been migrated merely because migration source files are present.
- Keep schema changes in EF Core migrations and review generated `Up`/`Down` operations and model snapshot before applying them.

Apply migrations from the repository root after providing `ConnectionStrings__QuickCommerceDb`:

```powershell
dotnet ef database update --project src/QuickCommerce.Infrastructure --startup-project src/QuickCommerce.Api
```

To create a migration:

```powershell
dotnet ef migrations add <MigrationName> --project src/QuickCommerce.Infrastructure --startup-project src/QuickCommerce.Api
```

## API surface

Controllers are in `src/QuickCommerce.Api/Controllers`. Current route groups include:

| Area | Route examples |
| --- | --- |
| Authentication | `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout`, `GET /api/auth/me` |
| Catalog and stores | `GET /api/categories`, `GET /api/products`, `GET /api/stores`, `GET /api/stores/nearest` |
| Cart and checkout | `/api/carts/{storeId}` and `POST /api/checkout/{storeId}` |
| Orders | `/api/orders`, `/api/customer/orders`, `/api/operations/orders/{orderId}/status` |
| Administration | `/api/admin/dashboard`, user/role/permission endpoints, `/api/admin/stores` |
| Approvals and notifications | `/api/approval-requests`, `/api/customer/notifications` |

See the controllers for request DTOs, authorization attributes/policies, response contracts, and exact query parameters. Do not infer that a route is suitable for public access from its URL; check its authorization configuration.

### Authentication flow

- Login issues a short-lived JWT access token and an opaque refresh token.
- Refresh tokens are stored as SHA-256 hashes and rotated when refreshed; logout revokes an active token.
- JWT signing configuration is read from `Jwt` settings. Outside Development, issuer, audience, signing key, and SQL connection configuration are required by startup validation.
- The API uses role/permission claims and named authorization policies for protected operations.
- Keep signing keys and connection strings in environment/secret configuration, never in source control.

### Local API

The documented HTTP launch profile is available with:

```powershell
$env:ConnectionStrings__QuickCommerceDb = "Server=localhost;Database=QuickCommerce;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet run --project src/QuickCommerce.Api --launch-profile http
```

The launch profile currently uses port `5067`. `QUICKCOMMERCE_TEST_CONNECTION_STRING` is a separate opt-in connection for SQL Server integration tests.

Health endpoints are `/health/live` and `/health/ready`. OpenAPI/Swagger is intended for Development.

## Development and validation

From the repository root:

```powershell
dotnet test
```

For the admin client, from `admin-portal`:

```powershell
npm install
npm run lint
npm run build
```

For the mobile client, from `mobile`:

```powershell
flutter pub get
flutter analyze
flutter test
```

The mobile README documents `flutter run -d chrome` as a local run target. Android builds require Android tooling; iOS builds require macOS/Xcode.

## Practical extension guidelines

- Keep HTTP/controller concerns in the API layer; put reusable business behavior behind Application interfaces and services.
- Keep EF-specific mappings and database work in Infrastructure. Add a migration for schema changes.
- Use the API response/DTO shape as the integration contract; inspect current endpoint implementations rather than relying on older roadmap notes.
- Keep UI widgets/components separate from client-side API and state concerns as the mobile app grows.
- Treat local seed/demo data as development support, not as production identity or catalog data.
- Read [Project Progress](./PROJECT_PROGRESS.md) before selecting the next feature; it records known implementation gaps and follow-up tasks.
