# ADR-001: Mobile data layer, HTTP client and DTO approach

**Status:** Accepted
**Date:** 2026-10-07
**Deciders:** Omkar (product/engineering owner)
**Resolves:** decision D12 in the [development plan](../MOBILE_DEVELOPMENT_PLAN.md)

## Context

The Flutter app was built UI-first on seed data. Phase P0 connects it to the ASP.NET Core API without letting HTTP, JSON or environment details leak into widgets. Constraints:

- The API wraps every response: `{ "success": true, "data": ... }` or `{ "success": false, "message": "...", "errors": [...] }`, and echoes `X-Correlation-ID`.
- Roughly 25 DTOs are expected across all phases (catalog, auth, cart, orders, addresses, payments).
- Project rules ask us to justify every new package and avoid unnecessary tooling.
- Small team; builds should stay fast and simple.

## Decision

1. **HTTP:** one `ApiClient` (`lib/data/api/api_client.dart`) on top of **Dio**. It unwraps the envelope, adds `X-Correlation-ID`, `Accept-Language` and an optional bearer token, and maps every failure to a typed `ApiException`.
2. **DTOs:** **hand-written** `fromJson` classes in `lib/data/dto/`. Parsing is strict; bad data raises a `FormatException`, which the client reports as `UnexpectedResponseException`. No code generation.
3. **Repositories:** screens depend on small interfaces (`CatalogRepository`, `HealthRepository`, later cart, orders, auth, address). Each has an API implementation and, where useful, a seed implementation for tests and offline work.
4. **Configuration:** `AppConfig` from `--dart-define-from-file=config/*.json`. `prod` must use https. No URLs or keys in source.
5. **Errors in the UI:** the UI shows `ApiException.userMessageKey` (translated), never the server `message` or a raw exception.
6. **Logging:** debug builds log method, path, status and correlation id only. Bodies and headers are never logged.
7. **State:** Riverpod providers (already in use) own repositories and screen state.

## Options considered

### DTOs

| Option | Complexity | New packages | Notes |
| --- | --- | --- | --- |
| **A. Hand-written (chosen)** | Low | none | Some boilerplate; mistakes caught by tests |
| B. `json_serializable` | Medium | `json_annotation`, `json_serializable`, `build_runner` | Less boilerplate; adds a generation step to every build and CI run |
| C. `freezed` + `json_serializable` | High | three more | Immutable unions and copyWith; heaviest tooling |

### HTTP client

| Option | Notes |
| --- | --- |
| **Dio (chosen)** | Interceptors, cancellation, timeouts and a mockable adapter. Needed for token refresh in P2 and search cancellation in P1. |
| `package:http` | Smaller, but we would rebuild timeouts, cancellation and interception ourselves. |

## Trade-offs

- Hand-written DTOs cost typing time now. They keep the toolchain minimal and make the API contract visible in plain code. If DTO count or churn grows, switching to `json_serializable` is local to `lib/data/dto/` and covered by the existing parsing tests.
- Dio is one new runtime dependency, accepted because no existing dependency provides interceptors or cancellation.
- Mapping all failures to a closed set of exception types means new server error shapes need a client change, but the UI stays simple and safe.

## Consequences

- Easier: testing without a network (`test/support/fake_adapter.dart`), consistent error handling, one place to add auth refresh, retry or caching.
- Harder: every new endpoint needs a DTO, a repository method and a test.
- Revisit: refresh-once on 401 (P2), response caching for catalog (P1 or P9), DTO generation if the count passes about 40.

## Action items

1. [x] `AppConfig`, `ApiClient`, typed exceptions, catalog DTOs, health and catalog repositories, providers.
2. [x] Debug-only API diagnostics screen.
3. [x] Unit tests for config, client, DTOs, repositories; Marathi coverage for error messages.
4. [ ] Move Home, Categories and Product screens onto repositories (P1).
5. [ ] Cart, orders, auth and address repository interfaces (added with their phases).
