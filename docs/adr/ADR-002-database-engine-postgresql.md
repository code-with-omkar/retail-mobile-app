# ADR-002: Database engine — SQL Server or PostgreSQL

**Status:** Accepted — option A (stay on SQL Server), decided 2026-10-07
**Date:** 2026-10-07
**Deciders:** Omkar (product/engineering owner)

## Context

`docs/copilot-instructions.md` sets **SQL Server** as the approved database (§33.3) and lists "replacing the database technology" as a stop-and-confirm change (§30, §33.9). While starting phase P0 the owner answered "we will use Postgres DB". No backend change has been made yet. This record captures the impact so the decision is made with full information.

What is in the repository today:

| Area | Current state |
| --- | --- |
| Provider | `Microsoft.EntityFrameworkCore.SqlServer` 10.0.5; `UseSqlServer` in `InfrastructureServiceRegistration.cs` |
| Migrations | 10 migrations plus a model snapshot, generated for SQL Server (`20260907115350_InitialCreate` to `20260909170835_StoreStaffApprovalWorkflow`) |
| Concurrency | `StoreInventory.RowVersion` optimistic concurrency and transactional order persistence (protected by Phase 1B rules) |
| Provider-specific mapping | 6 entity configurations reference SQL Server types or row versions (cart, cart item, notification, order, order status history, store inventory) |
| Health | `SqlServerHealthCheck` backs `/health/ready` |
| Tests | `PersistenceModelTests` and `ProductionReadinessTests` use SQL Server; SQL integration test reads `QUICKCOMMERCE_TEST_CONNECTION_STRING` |
| Docs and ops | README, TECHNICAL_OVERVIEW, PROJECT_PROGRESS, IIS deploy notes and the engineering rules all name SQL Server |
| This machine | SQL Server (default and SQLEXPRESS) and PostgreSQL 18 services are both running |

## Questions to answer first

1. What is the reason for PostgreSQL (licence cost, hosting, team skill, existing server)? It decides whether the cost below is worth paying.
2. Does any environment hold data that must be kept, or is everything still development data?
3. Is the production target still IIS with several instances behind a load balancer (the rules say yes)? That works with PostgreSQL, but backup, high availability and monitoring would need to be re-planned.

## Options

### A. Stay on SQL Server
No change. Matches the rules, migrations, tests and docs.

### B. Move to PostgreSQL now (before phase P1)
Replace the provider, regenerate migrations for PostgreSQL, rework the concurrency token, update tests and docs. Cheapest now, because P1 to P8 will each add migrations that would otherwise have to be written for both engines.

### C. Support both engines
Provider-specific migration sets and a test matrix. Highest ongoing cost and not recommended for a small team.

| Dimension | A | B | C |
| --- | --- | --- | --- |
| Effort now | None | Medium to high | High |
| Ongoing effort | Low | Low after move | High |
| Risk | Low | Medium (concurrency, data) | High |
| Matches current rules | Yes | Needs rules update | Needs rules update |

## What option B involves

1. Swap the EF provider to `Npgsql.EntityFrameworkCore.PostgreSQL` (a new package; replaces the SQL Server one).
2. Replace `SqlServerHealthCheck` with a provider-neutral database check.
3. **Migrations:** the existing 10 cannot run on PostgreSQL. Create a new baseline for PostgreSQL from the current model and keep the old set in the repository history. Existing SQL Server data is not carried over unless a data copy is planned and approved.
4. **Concurrency:** SQL Server `rowversion` has no direct equivalent. PostgreSQL uses the `xmin` system column as the concurrency token, which changes the property type on `StoreInventory`. Behaviour must stay identical (no overselling), proved by concurrent-checkout tests before and after.
5. Review the 6 configurations and the seed data (deterministic GUIDs are fine) for provider-specific types and casing.
6. Update tests (remove `UseSqlServer`; use the provider-neutral connection variable) and run the full suite.
7. Update the engineering rules (§33.3, §34.2), TECHNICAL_OVERVIEW, README and IIS notes; replace Windows authentication with a secret-managed login.
8. Mobile and Angular clients are unaffected: they only see the REST API.

## Recommendation

If PostgreSQL is a real requirement, do it **now**, as one reviewed change, before P1 adds more migrations. If there is no strong reason, stay on SQL Server and avoid the cost. Either way, record the answer here and in decision D13.

## Consequences if B is approved

- Tasks 0.10 to 0.13 in the dashboard become the P0 database work, and 0.1 (run the API locally) uses PostgreSQL.
- The Phase 1B protection list is respected in behaviour (transactional orders, optimistic concurrency, deterministic seed) while the implementation changes.

## Action items

1. [ ] Owner answers the three questions and confirms A or B.
2. [ ] If B: write the mini-plan for tasks 0.10 to 0.13 and wait for approval.
3. [ ] Update `copilot-instructions.md` to match the decision.

## Decision (2026-10-07)

The owner chose to **keep SQL Server**. No provider, migration or concurrency changes are made. The PostgreSQL tasks (0.10 to 0.13) were removed from the plan. The analysis above is kept in case the question returns.
