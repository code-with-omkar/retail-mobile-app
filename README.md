# QuickCart Retail Platform

QuickCart is a modular-monolith quick-commerce platform with a customer mobile app, an operations/admin portal, and an ASP.NET Core API.

## Project areas

- `mobile`: Flutter customer-app prototype. The current app is a local-data home-screen slice, not yet an API-connected shopping app.
- `admin-portal`: Angular 20 and TypeScript operations portal with authentication and management features.
- `src/QuickCommerce.Api`: ASP.NET Core 10 REST API.
- `src/QuickCommerce.Application`: application services, interfaces, request DTOs, and validation.
- `src/QuickCommerce.Domain`: domain entities.
- `src/QuickCommerce.Infrastructure`: EF Core SQL Server persistence, migrations, authentication, and optional Redis caching.
- `src/QuickCommerce.Tests`: .NET unit and persistence tests.

## Project documentation

- [Project progress and pending work](docs/PROJECT_PROGRESS.md)
- [Technical overview and development reference](docs/TECHNICAL_OVERVIEW.md)
- [IIS deployment notes](deploy/iis/README.md)
- [Admin portal notes](admin-portal/README.md)
- [Mobile app notes](mobile/README.md)

## Run locally

Configure a SQL Server connection string in the environment before starting the API. For example, using Windows integrated authentication:

```powershell
$env:ConnectionStrings__QuickCommerceDb = "Server=localhost;Database=QuickCommerce;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet run --project src/QuickCommerce.Api --launch-profile http
```

Apply migrations when creating or updating the local database:

```powershell
dotnet ef database update --project src/QuickCommerce.Infrastructure --startup-project src/QuickCommerce.Api
```

The API launch profile uses `http://localhost:5067`. Start the admin portal in another PowerShell session:

```powershell
cd admin-portal
npm install
npm run dev
```

The admin portal's API URL is configurable; set it to match the API address for your environment.

For the Flutter customer-app scaffold:

```powershell
cd mobile
flutter pub get
flutter run -d chrome
```

## Validation

```powershell
dotnet test
```

In `admin-portal`:

```powershell
npm run lint
npm run build
```

Set `QUICKCOMMERCE_TEST_CONNECTION_STRING` separately to enable SQL Server integration tests. Do not commit connection strings, JWT signing keys, or other environment secrets.
