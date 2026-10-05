# QuickCart Project Progress

This document records the implementation visible in the repository and separates it from work that is still outstanding. It is a codebase snapshot, not a statement that a production environment has been deployed or configured.

## Current status

The backend and admin portal have progressed beyond their original demo-only roadmap. SQL Server persistence, migrations, authentication, authorization, and core commerce APIs are present. The customer Flutter app remains an early UI prototype backed by hard-coded local data.

## Implemented

### API and backend

- ASP.NET Core 10 API organized into API, Application, Domain, and Infrastructure projects.
- EF Core SQL Server context, entity configurations, seed data, and migrations for commerce, organization/store, cart/order, notification, authorization, identity, and approval foundations.
- Catalog endpoints for categories, products, and stores, including nearest-store lookup.
- Cart and checkout services, plus order creation and customer order tracking.
- Order lifecycle/status operations and status history.
- JWT login, access-token refresh, logout, and current-user endpoint. Refresh tokens are stored as hashes; login failure tracking and temporary credential lockout are implemented.
- Role/permission management, authorization scopes/policies, and store-staff approval requests.
- Customer notification endpoints and an admin dashboard endpoint.
- Request correlation/logging, standardized exception responses, health endpoints, and development OpenAPI/Swagger.
- Optional Redis-backed distributed cache. Without a Redis connection, the API registers a no-op cache implementation.

### Admin portal

- Angular 20 standalone application with lazy-loaded routes, authentication guards, role checks, shared UI components, and JWT request handling.
- Feature areas include dashboard, catalog/products, orders, stores, user/role/permission management, approvals, profile, login, and notifications.
- API integration and configurable API base URL are present. Some areas still contain placeholders or need end-to-end verification against a configured API.

### Customer mobile app

- Flutter/Material 3 home-screen prototype with a delivery-location label, local search, category rail, sample product cards, in-memory quantity controls, and bottom navigation.
- The products and quantities are local to the screen; the bottom navigation does not yet provide the complete cart, orders, or profile journeys.

### Tests

- .NET tests cover selected catalog/cache, cart, checkout, order workflow, customer tracking, authorization, security, validation, persistence-model, and production-readiness behavior.
- A SQL Server integration test can be enabled with `QUICKCOMMERCE_TEST_CONNECTION_STRING`.
- Flutter and Angular each have their platform test/build tooling, but full end-to-end coverage is not established by the presence of those tools alone.

## Pending work

Items below are based on the code and documentation present in this repository. Prioritize them according to product requirements.

### Priority 1 — Complete a usable customer shopping journey

- Split the single-screen Flutter prototype into feature modules and add navigation/state management.
- Replace local sample products and search with catalog API integration; load categories and make category selection functional.
- Implement customer registration/password recovery if required; connect login and session management to the existing API.
- Implement delivery-address/location selection and use selected coordinates to find a serviceable store.
- Connect a store-aware cart to the cart API and implement cart review.
- Connect checkout to the backend, including stock/price revalidation and clear failure handling.
- Implement payment-provider integration; payment is not implemented by the current mobile prototype.
- Add order history/detail/status tracking and profile/logout experiences.

### Priority 2 — Finish portal-to-API integration

- Connect the notifications page to the existing notifications API; the current page explicitly renders a placeholder.
- Verify every admin/staff screen against API behavior, including loading, empty, error, permission-denied, and success states.
- Configure the Angular API base URL per environment and verify it matches the API host/port used in each run or deployment.
- Complete remaining visible interaction placeholders such as global search where they are in product scope.

### Priority 3 — Release readiness

- Apply and verify database migrations in each target environment; repository migrations do not prove that a particular database is current.
- Configure production JWT issuer, audience, signing key, and SQL Server connection outside source control; restrict and verify the production CORS policy, and configure Redis if it is required.
- Add automated integration/end-to-end coverage for the mobile/API and admin/API journeys, including payment failure and inventory conflict paths.
- Run the API tests (including SQL integration tests where available), admin lint/build, and Flutter analysis/tests before releases.
- Validate deployment, health checks, logs, database backup/recovery, and operational monitoring in the target environment.

## Local validation commands

From the repository root:

```powershell
dotnet test
```

From `admin-portal`:

```powershell
npm run lint
npm run build
```

From `mobile`:

```powershell
flutter analyze
flutter test
```

The SQL Server integration test is opt-in and requires `QUICKCOMMERCE_TEST_CONNECTION_STRING`.
