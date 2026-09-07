# IIS Deployment Foundation

The CI workflow publishes `QuickCommerce.Api` as a zip artifact. Deployment runs only from a manually approved `production` GitHub Environment on a self-hosted Windows runner labeled `iis`.

## IIS prerequisites

- IIS with the ASP.NET Core Hosting Bundle for .NET 10 installed.
- Two IIS sites/applications and application pools already created for the API: one Blue and one Green.
- The runner account can manage the application pool and write to the deployment directory.
- Each IIS instance has the `iis` runner label. Run the same Blue-Green deployment independently on each instance; load-balancer configuration remains outside this repository.
- Both slot health URLs and the public traffic health URL are reachable from the deployment runner.
- Configure IIS Instance 1 and every additional instance with the same Blue and Green slot layout and equivalent environment settings.
- All instances must use the same shared SQL Server database and the same shared Redis deployment.
- The API does not use HTTP session state, instance-local business state, or local-memory production caching. Sticky sessions are not required.

The .NET Web SDK generates the deployable `web.config` in the published output. Do not commit production connection strings, JWT keys, Redis credentials, or environment-specific `appsettings.Production.json` files.

## GitHub Environment variables

Configure these as non-secret variables in the `production` Environment:

- `BLUE_GREEN_ACTIVE_SLOT`: `Blue` or `Green`, maintained in the deployment environment as the current traffic slot.
- `BLUE_SITE_NAME`, `GREEN_SITE_NAME`: Existing IIS site/application names.
- `BLUE_APP_POOL_NAME`, `GREEN_APP_POOL_NAME`: Existing slot application pool names.
- `BLUE_DEPLOY_PATH`, `GREEN_DEPLOY_PATH`: Separate physical paths, such as `D:\Sites\QuickCommerce.Api.Blue` and `D:\Sites\QuickCommerce.Api.Green`.
- `BLUE_HEALTH_LIVE_URL`, `GREEN_HEALTH_LIVE_URL`: Direct `/health/live` URLs for each slot.
- `BLUE_HEALTH_READY_URL`, `GREEN_HEALTH_READY_URL`: Direct `/health/ready` URLs for each slot.
- `TRAFFIC_HEALTH_URL`: Public load-balanced `/health/ready` URL used after switching traffic.
- `TRAFFIC_SWITCH_SCRIPT`: Path to an operator-managed, vendor-specific traffic switch hook on the self-hosted runner.

Configure application settings on the IIS host or through the hosting platform environment. Typical variables are:

- `ASPNETCORE_ENVIRONMENT=Production`
- `ConnectionStrings__QuickCommerceDb`
- `Jwt__Issuer`
- `Jwt__Audience`
- `Jwt__SigningKey`
- `Caching__RedisConnectionString` (optional)

Every instance must define the same database, JWT, and Redis settings. Redis is optional only when caching is intentionally disabled; when enabled, it must point to the shared Redis deployment rather than an instance-local process.

Secrets must be supplied by the GitHub Environment, IIS machine-level environment, or an approved secret store. They must not be placed in workflow YAML, source control, or the artifact.

## Pipeline usage

1. Push or open a pull request to run restore, build, and test validation.
2. A successful validation publishes an IIS zip artifact retained for 14 days.
3. Start the workflow manually with `deploy_to_iis=true` after selecting the approved `production` Environment.
4. The self-hosted IIS runner downloads the exact artifact from that workflow run.
5. `Deploy-QuickCommerceApiBlueGreen.ps1` selects the slot opposite `BLUE_GREEN_ACTIVE_SLOT`.
6. The inactive slot is backed up, stopped, replaced, and started; the active slot is not modified.
7. The script verifies inactive `/health/live`, inactive `/health/ready`, and active pre-switch readiness.
8. The external traffic hook switches traffic to the inactive slot.
9. The script verifies the public `TRAFFIC_HEALTH_URL` and retains the previous slot for immediate rollback.

The workflow stops before publishing or deployment when restore, build, or tests fail.

Production deployment does not run `dotnet ef database update` or apply EF migrations. Database migrations require a separate reviewed operation.

The traffic switch hook must accept `-FromSlot` and `-ToSlot` parameters and return a non-zero exit code when switching or verification fails. Its implementation is intentionally outside this repository so the same deployment script can support different load-balancer providers.

## Multi-instance load-balancer guidance

Use a generic pool of IIS instances behind the load balancer:

- IIS Instance 1, Instance 2, and any later instances run the same published API artifact.
- Each IIS instance has a Blue and Green application. Only one slot receives production traffic at a time.
- Route normal API traffic to the active slot on all healthy IIS instances.
- Probe each instance directly at `/health/live` for process liveness and `/health/ready` for traffic readiness.
- Remove an instance from rotation when `/health/ready` is not HTTP 200; `/health/live` only indicates that the process is running.
- Readiness checks SQL Server and, when configured, Redis. Both dependencies must be reachable from every instance.
- Keep the probe paths unauthenticated and exclude them from application authorization rules.
- Do not depend on sticky sessions. Authentication is carried in each request and shared application state is held in SQL Server or Redis.
- Keep Blue/Green slot selection consistent across instances. Switch the load balancer only after every target instance has the inactive slot started and ready.
- Configure TLS termination, forwarding headers, timeouts, and vendor-specific probe syntax outside this repository.

Before adding an instance, verify its environment variables, shared dependency connectivity, and direct `/health/ready` response. After deployment, verify every instance individually before returning it to rotation.

## Safe traffic switch and rollback

The active slot remains untouched while the new release is installed in the inactive slot. Traffic is switched only after both slot health checks pass. The previous slot remains started and available.

Rollback is automatic when inactive deployment, startup, either inactive health probe, the traffic switch hook, or the public post-switch readiness check fails. If traffic has already switched, the script invokes the hook to switch from the new slot back to the previous slot, verifies `TRAFFIC_HEALTH_URL`, and restores the failed inactive slot files from its timestamped backup.

For a manual rollback, invoke the traffic hook with `-FromSlot <current-slot> -ToSlot <previous-slot>`, verify the public readiness URL, and leave the previous slot serving traffic. Update `BLUE_GREEN_ACTIVE_SLOT` only after the switch is confirmed. Do not delete the previous slot until the release has passed its observation window.
