# Change protocol

How every task in the [development plan](./MOBILE_DEVELOPMENT_PLAN.md) is run. It applies the rules in [copilot-instructions.md](./copilot-instructions.md) (sections 2, 28, 29, 30 and 34.12). If this page and that file disagree, that file wins.

```text
UNDERSTAND -> PLAN -> MINIMAL CHANGE -> TEST -> VERIFY -> REPORT
```

## 1. Mini-plan (before editing)

Copy this into the task notes in the dashboard or the pull request description.

```markdown
### Task <id>: <title>
- What will change:
- Why:
- Files likely to change:
- Database changes (additive? migration name? rollback?):
- API changes (new or changed routes/DTOs? backward compatible?):
- UI changes:
- Risks:
- Existing functionality that could be affected:
- Tests to add or update:
```

## 2. Stop and ask when the change would

- drop tables or data, or rename tables or columns
- break an existing API contract
- change authentication architecture
- replace a core technology (database, framework, hosting)
- change the global UI theme
- restructure projects or do a large refactor

## 3. API change checklist

Before changing a controller, DTO or route:

- [ ] Who calls it? Mobile app, **admin portal** (`admin-portal/src/app/core/config/api-config.ts`), tests.
- [ ] Is the change additive (new field, new route) rather than renaming or removing?
- [ ] Is authorization enforced on the server, with store/organization scope checked from the signed-in user rather than client IDs?
- [ ] Is a validator in Application covering the new input?
- [ ] Are `docs/` and the API contract file updated?

## 4. Database change checklist

- [ ] Inspected current tables, relationships, indexes and data first.
- [ ] Migration is additive and reviewed (`Up` and `Down`, model snapshot).
- [ ] Not auto-run in production; a reviewed script is applied per environment.
- [ ] Orders still keep price snapshots; inventory changes stay safe under concurrent orders.

## 5. Mobile change checklist

- [ ] Screens use repositories, not HTTP or seed data directly.
- [ ] New user-visible strings go through `context.tr(...)` and have a Marathi entry (the `l10n_test` fails otherwise).
- [ ] Loading, empty and error states exist.
- [ ] `powershell -File mobile/tool/check.ps1` (or `sh mobile/tool/check.sh`) passes.

## 6. Done report

1. Files added, changed, deleted
2. Database changes
3. API changes
4. UI changes
5. Tests added or updated
6. What could not be verified
7. Recommended follow-ups (listed separately, not done)

Never commit secrets, connection strings with credentials, API keys, or signing keys.

## 7. Running the extra tests

```powershell
# Mobile quality gate (analyze + all unit and widget tests)
powershell -File mobile/tool/check.ps1

# Live smoke test: starts the API if it is not running, tests it, stops it again
powershell -File mobile/tool/smoke.ps1

# SQL Server integration test for the catalog queries. Use a DISPOSABLE database: it applies migrations and adds/removes rows.
$env:QUICKCOMMERCE_TEST_CONNECTION_STRING = "Server=localhost;Database=QuickCommerce_Test;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet test QuickCommerce.slnx --filter CatalogEfIntegrationTests
```

Without the environment variable the SQL integration tests do nothing and pass, so ordinary `dotnet test` needs no database.
