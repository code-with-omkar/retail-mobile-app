# QuickCart Retail Platform

A modular-monolith quick-commerce foundation for customer, store staff, and admin experiences.

## Projects

- `src/QuickCommerce.Domain`: catalog, store, inventory, and order domain models.
- `src/QuickCommerce.Application`: nearest-serviceable-store business logic and distance calculation.
- `src/QuickCommerce.Infrastructure`: seeded store/inventory gateway used for the runnable demo; replace with EF Core SQL Server persistence in the next slice.
- `src/QuickCommerce.Api`: ASP.NET Core REST API with consistent `{ success, data }` responses.
- `admin-portal`: Angular LTS + TypeScript responsive operations dashboard.
- `mobile`: Flutter handoff contract and planned customer app boundaries.

## Run locally

```powershell
dotnet run --project src/QuickCommerce.Api --launch-profile http
```

The API is available at `http://localhost:5067`. In another terminal:

```powershell
cd admin-portal
npm install
npm run dev
```

For local SQL Server development, provide the connection string through the environment so credentials remain outside source control:

```powershell
$env:ConnectionStrings__QuickCommerceDb = "Server=localhost;Database=retail-mobile-app;User Id=sa;Password=<local-secret>;TrustServerCertificate=True;"
dotnet ef database update --project src/QuickCommerce.Infrastructure --startup-project src/QuickCommerce.Api
```

Set `QUICKCOMMERCE_TEST_CONNECTION_STRING` separately to run the opt-in SQL Server integration test.

The dashboard uses live API data when the API is available and falls back to demo catalog data otherwise.

## API examples

- `GET /api/products?search=milk`
- `GET /api/categories`
- `GET /api/stores/nearest?latitude=19.076&longitude=72.8777&productIds={id}`
- `POST /api/orders`
- `GET /api/orders/{id}`

Order creation locks the shared inventory store, validates every requested line, decrements store-specific stock, snapshots product names/prices, and appends initial status history. The next persistence slice should move this exact workflow into an EF Core transaction with row-version concurrency.

## Roadmap

1. Replace the demo gateway with EF Core SQL Server migrations and row-version concurrency.
2. Add JWT identity, role policies, FluentValidation, and admin/store-staff authorization.
3. Add cart and customer/mobile flows, then order status transitions and history.
4. Add focused unit tests for distance, store selection, inventory conflicts, and price snapshots.
