# Mission Control agent guide

## Purpose

Mission Control is a .NET 10 operations system. It accepts authenticated integration events, routes them through NATS JetStream, archives and projects selected events, collects current host/container/protocol telemetry on multiple Agents, and presents current and historical state through an authenticated Dashboard and a MAUI Blazor Hybrid Mobile app.

## Solution map

| Project | Responsibility |
| --- | --- |
| `MissionControl.Gateway` | Authenticated generic event ingress and signed GitHub webhook ingress; normalizes and publishes event envelopes. |
| `MissionControl.Messaging` | Broker-independent event publisher and processor abstractions. |
| `MissionControl.Messaging.Nats` | NATS connection, JetStream stream initialization, publishing, durable consumption, and delivery handling. |
| `MissionControl.Archive` | Durable event consumer, SQLite envelope archive, and archive query API. |
| `MissionControl.GitActivity` | Durable GitHub push-event consumer, allowed repository/branch projection in SQLite, and API-key-protected feed. |
| `MissionControl.Agent` | Periodic host, Docker, and protocol collection; latest-snapshot SQLite storage; Agent HTTP API; selected event publication. |
| `MissionControl.Dashboard` | Authenticated Blazor Server host, fleet aggregation, service catalog, upstream integrations, and authenticated Mobile API. |
| `MissionControl.Mobile` | .NET MAUI Blazor Hybrid client. It reaches server-side data through the Dashboard Mobile API. |
| `MissionControl.Contracts` | Dependency-light wire/data contracts shared across process boundaries. No HTTP behavior or UI state belongs here. |
| `MissionControl.Client` | Typed HTTP clients, client-facing integration models, and reusable client-side feed/refresh state. |
| `MissionControl.UI` | Shared Razor components, presentation helpers, catalog view building, and theme used by Dashboard and Mobile. |
| `MissionControl.Observability` | Shared health endpoint mapping and NATS health checks. |
| `MissionControl.Tests` | xUnit unit and focused integration coverage across the solution. |

## Architectural boundaries

- Treat `Contracts` as serialized compatibility surface. Keep it free of host-specific dependencies and change existing shapes deliberately.
- Put transport behavior in `Client`, not `Contracts`. `Client` may expose client-only models when they are not server event contracts.
- Put reusable presentation and mapping logic in `UI`. Dashboard and Mobile retain their own pages, navigation, authentication, lifecycle, and host-specific state.
- Dashboard owns server-side upstream URLs and, where required, credentials for Archive, GitActivity, GreenCloud, Gateway publication, and Work Planning. Mobile must not receive private service credentials or URLs.
- Dashboard pages are cookie-authenticated. Mobile calls bearer-authenticated Dashboard endpoints; it does not call Archive, GitActivity, Agents, GreenCloud, or Work Planning directly.
- Archive and GitActivity consume durable events. They are not the source of live Agent state.

## Current-state and event models

Mission Control intentionally has a hybrid Agent architecture:

- An Agent collects a snapshot, stores the latest snapshot locally, and serves it from `GET /api/snapshot`.
- Dashboard queries every configured Agent directly for current state, normally around `Dashboard:Refresh:AgentSnapshotRefreshSeconds` (30 seconds by default).
- An Agent also publishes `missioncontrol.agent.node.snapshot` through Gateway when first due, when operational state changes, or on its heartbeat.
- Gateway publishes to JetStream; Archive can retain that event. Mission Control does not yet centrally store all current Agent state.

Do not replace live Agent reads with Archive reads unless a task explicitly changes that architecture.

## Fleet identity and service matching

- `Agent:NodeId` is the stable machine identity. `Agent:NodeName` is the display name; `EffectiveNodeId` falls back to `NodeName` only for compatibility when `NodeId` is absent.
- Dashboard `Agents:Nodes` config owns the fleet list and the Dashboard-facing `NodeId`, display name, and Agent base URL. Node IDs are unique case-insensitively.
- Service catalog `NodeId` assigns a service to one fleet node. The existing `clanker` default is a compatibility behavior; make node ownership explicit in new entries.
- After selecting a node, match `ContainerName` to that node's Agent container name and `ProtocolServiceKey` to that node's protocol service key, case-insensitively.
- Container names and protocol keys must be unique per node, not globally. The same name may legitimately exist on Clanker and ScopeCreep.
- GreenCloud `Servers[].NodeId` uses the same fleet key to associate provider bandwidth with a host.

## GreenCloud

GreenCloud fleet bandwidth is Dashboard-owned cached state:

- `GreenCloudBandwidthPollingService` refreshes once at startup and then at `GreenCloud:PollSeconds`.
- `GreenCloudBandwidthFleetRefreshController` is a singleton, suppresses overlapping refreshes, and preserves last-known per-node data when a refresh fails.
- Dashboard Hosts and `GET /api/mobile/hosts` read the cached state. Mobile host requests must never trigger a GreenCloud fleet refresh.
- Agent refresh and GreenCloud refresh are independent; a slow provider must not delay healthy Agent telemetry.
- The authenticated `GET /api/mobile/bandwidth` single-server compatibility endpoint still performs a direct provider request. Do not broaden or remove compatibility paths casually; change them only with an explicit migration requirement.

## Dashboard and Mobile shared UI

- Reuse `MissionControl.UI` components and presentation helpers when both hosts show the same concept.
- Keep server-only services, authentication, polling ownership, and configuration in Dashboard.
- Keep MAUI storage, Mobile API authorization, navigation, and device lifecycle in Mobile.
- The Mobile app bundles Dashboard's `services.json` at build time; Dashboard reloads its copy at runtime. A catalog edit therefore reaches Dashboard immediately after a valid reload but requires a Mobile rebuild to update the bundled catalog.

## Async, cancellation, and disposal

- Pass request, host-shutdown, and component-disposal cancellation tokens through all async I/O.
- Rethrow `OperationCanceledException` when the supplied token was canceled. Convert only expected timeout/provider failures into degraded state.
- Dispose `HttpResponseMessage`, timers, linked token sources, streams, and other owned resources.
- Razor pages that start polling must cancel, await, and dispose their polling work in `IAsyncDisposable`. Avoid callbacks after disposal.
- Use the existing refresh gates/controllers to suppress overlap. Singleton cached state must remain safe under concurrent Dashboard, Mobile, and hosted-service access.
- Isolate independent collectors/providers so one failure does not discard unrelated healthy data. Preserve last-known data where the existing UI contract supports it and surface a scoped warning.

## Tests and validation

Add focused tests with behavior changes. Cover success plus relevant cancellation, timeout, partial failure, stale/last-known, authentication, serialization, and concurrency behavior. Tests use controlled HTTP/NATS/Docker/protocol doubles and temporary SQLite databases; they should not require production services or credentials.

Common commands from the repository root:

```powershell
dotnet restore MissionControl.slnx
dotnet build MissionControl.slnx -c Debug
dotnet test MissionControl.Tests/MissionControl.Tests.csproj -c Debug
dotnet format MissionControl.slnx --verify-no-changes
```

Prefer a targeted test filter while iterating, then run the full test project for cross-project changes. The MAUI project is multi-targeted; when validating a platform-specific change, build the relevant target explicitly. On Windows, `scripts/Build-MissionControlWindows.ps1` is the repository-provided Windows build helper.

## Configuration, secrets, and deployment

- Executables use standard .NET configuration. Environment variables use `__` for nested keys.
- Checked-in JSON is defaults/placeholders, not a credential store. Keep event-source keys, webhook secrets, GitActivity and Work Planning API keys, GreenCloud tokens, Mobile raw tokens/token hashes, signing keys, and production connection credentials in user secrets, environment variables, or the deployment secret provider.
- Do not log or return upstream credentials, private URLs, raw exception details, or deployment-sensitive data to Mobile.
- SQLite databases, ASP.NET Data Protection keys, and JetStream storage are persistent operational state.
- Production Docker Compose is maintained outside this repository in `JoyfulReaper/UsefulScripts`.
- `Dockerfile.agent` is a build/deployment aid. Its presence does not mean the current production Agents are necessarily containerized.

## Compatibility and workflow

- Preserve public contracts, routes, configuration keys, event types, cursor semantics, fleet IDs, and stored data deliberately. Do not add a legacy fallback or parallel path just because it is easy; add one only for a known compatibility need and test it.
- Work on the branch requested by the task. At the time this guide was written, the active integration branch was `dev`.
- No repository-specific pull-request, merge, or CI workflow policy is checked in. Do not invent one. Preserve unrelated working-tree changes and keep commits/tasks focused.
- Do not edit README or architecture documentation unless the task includes documentation updates.
