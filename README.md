# Mission Control

Mission Control is a .NET 10 operations system for integration-event history and live infrastructure visibility. It accepts authenticated events, publishes them through NATS JetStream, archives complete envelopes in SQLite, projects selected GitHub activity, collects current host/container/protocol state from a fleet of Agents, and presents that state through an authenticated Blazor Dashboard and MAUI Blazor Hybrid Mobile app.

Mission Control deliberately uses a hybrid Agent architecture: Dashboard queries Agents directly for live state, while Agents also publish selected operational events through Gateway for durable processing. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for component boundaries, detailed data flows, and current limitations. Coding agents should also read [AGENTS.md](AGENTS.md).

## Solution projects

| Project | Purpose |
| --- | --- |
| `MissionControl.Gateway` | ASP.NET Core ingress for generic integration events and signed GitHub webhooks. Publishes normalized envelopes to NATS JetStream. |
| `MissionControl.Archive` | NATS JetStream consumer and HTTP query API backed by a SQLite event archive. |
| `MissionControl.Agent` | Collects host, Docker, and protocol status; persists the latest node snapshot; optionally publishes operational snapshot events; exposes a sanitized snapshot API. |
| `MissionControl.Dashboard` | Authenticated Blazor Server UI and integration host for the Agent fleet, service catalog, GreenCloud bandwidth, Archive, GitActivity, Work Planning, and the bearer-authenticated Mobile API. |
| `MissionControl.GitActivity` | Consumes selected GitHub push events, stores an allowed repository/branch projection in SQLite, and exposes an API-key-protected activity feed. |
| `MissionControl.Contracts` | Shared Agent, Archive, GitActivity, service-catalog, integration-event, and GitHub transport contracts. |
| `MissionControl.Client` | Shared typed HTTP clients and client-side state for Agent, Archive, GitActivity, host fleet, bandwidth, and Work Planning APIs. |
| `MissionControl.UI` | Razor Class Library with shared infrastructure, service, event, Git Activity, Work Planning, and overview components plus the shared theme. |
| `MissionControl.Mobile` | .NET MAUI Blazor Hybrid application that shares Client, Contracts, and UI and reaches server-side data through Dashboard's authenticated Mobile API. |
| `MissionControl.Messaging` | Broker-independent event publisher and integration-event processor contracts. |
| `MissionControl.Messaging.Nats` | NATS.Net 3.1.0 publisher, JetStream initialization, durable consumer, and messaging configuration. |
| `MissionControl.Observability` | Shared liveness/readiness endpoint mapping plus NATS JetStream and consumer health checks. |
| `MissionControl.Tests` | xUnit unit and focused integration coverage for storage, messaging, Agent, Dashboard, shared client/UI behavior, GitActivity, and API contracts. |

## Data flow

The primary integration-event path is:

```text
generic client or GitHub
-> MissionControl.Gateway
-> NATS JetStream stream MISSION_CONTROL_EVENTS
   -> mission-control-archive durable consumer
      -> MissionControl.Archive
      -> SQLite event archive
      -> Archive HTTP API
      -> MissionControl.Dashboard
   -> mission-control-git-activity durable consumer
      -> MissionControl.GitActivity
      -> SQLite Git activity projection
```

The Gateway publishes each `IntegrationEventEnvelope` to
`events.{EventType}`. The application creates or updates the
`MISSION_CONTROL_EVENTS` stream idempotently with subject `events.>`, file
storage, limits retention, `DiscardOld`, a 7-day maximum age, a 256 MiB byte
limit, and one replica. The envelope `EventId` is used as the JetStream message
ID so JetStream can deduplicate repeated publishes.

The shared cross-platform UI layers sit above those service APIs:

```text
MissionControl.Contracts
-> MissionControl.Client
-> MissionControl.UI
   -> MissionControl.Dashboard authenticated Blazor web app
   -> MissionControl.Mobile Windows MAUI Blazor Hybrid app
   -> MissionControl.Mobile Android MAUI Blazor Hybrid app
```

`MissionControl.GitActivity` consumes `events.github.push.received` from its own
durable JetStream consumer. It filters configured repositories and branches, then stores a
commit-oriented SQLite projection. Dashboard and Mobile use the private service
path:

```text
authenticated Dashboard page
-> Dashboard server-side GitActivity client with X-Mission-Control-Key
-> private GET /api/github/activity

Windows / Android MAUI client
-> HTTPS Dashboard Mobile API with bearer token
-> GET /api/mobile/git-activity
-> Dashboard server-side GitActivity client with X-Mission-Control-Key
-> private GET /api/github/activity
```

The Dashboard holds the GitActivity API key. Installed MAUI clients hold only
the Dashboard Mobile bearer token; the proxy never returns the GitActivity key
or service URL. A deployment may separately expose the API-key-protected
GitActivity endpoint for trusted server-side consumers, but Mobile does not use
that route.

### JetStream consumers and delivery behavior

| Service | Durable consumer | Filter subject |
| --- | --- | --- |
| Archive | `mission-control-archive` | `events.>` |
| GitActivity | `mission-control-git-activity` | `events.github.push.received` |

Both consumers use explicit acknowledgements, allow at most one unacknowledged
message (`MaxAckPending: 1`), and are configured for at most five deliveries.
Successful processing sends an ACK. A malformed outer envelope or a permanent
processor failure sends TERM and is not retried. Other processor failures send
a delayed NAK and are eligible for redelivery after 30 seconds. The NATS client
retries its initial connection and uses its reconnect behavior for connection
interruptions; the consumer service also restarts its consume loop after a
failure.

The live Agent path is separate from durable event history:

```text
host, Docker, and protocol collectors on each node
-> AgentWorker
-> latest snapshot in that Agent's SQLite database
-> GET /api/snapshot on that Agent
-> Dashboard fleet client
-> Dashboard Home / Hosts / Services
```

Dashboard's `Agents:Nodes` configuration defines the fleet by stable `NodeId`, display name, and Agent base URL. It queries configured nodes concurrently and preserves healthy or last-known node data when another node is unavailable. The current configuration uses Clanker and ScopeCreep as example fleet nodes.

Agents also publish `missioncontrol.agent.node.snapshot` events through Gateway. Every Agent collection is saved locally, but an event is published only for the first successful attempt, an operational-state change, or the configured heartbeat. CPU, memory, container resource usage, probe duration, and diagnostic wording alone do not trigger publication. A failed publication remains eligible for the next collection.

Installed Mobile clients do not contact Agents or private services directly:

```text
MAUI client
-> HTTPS Dashboard Mobile API with bearer token
-> Dashboard fleet and integration clients
-> configured Agents, Archive, GitActivity, and Work Planning services
```

Mobile polls the authenticated `GET /api/mobile/hosts` endpoint for fleet telemetry. Dashboard refreshes Agent state for that request and combines it with the latest server-side cached GreenCloud bandwidth state. Keep Agent, Archive, GitActivity, Work Planning, and provider credentials behind Dashboard rather than exposing them for Mobile.

## Agent monitoring

The Agent currently collects:

- logical processor count, CPU usage, and memory totals/availability from Linux `/proc` data;
- all Docker containers, including running, stopped, exited, created, and restarting states;
- image, restart count, and resource metrics when the Docker API supplies them;
- Echo, QOTD, Gopher, Finger, and Daytime protocol checks with per-probe timeouts.

Docker collection uses a Unix domain socket, normally `/var/run/docker.sock`. Docker is disabled by default on Windows. On non-Linux hosts the Agent reports logical processor count, but CPU and memory values are unavailable. A Docker outage or an individual container-statistics failure does not discard host and protocol results.

For a host-installed Linux Agent, the service account must be able to open the
Docker socket. On systems where `/var/run/docker.sock` is owned by
`root:docker`, add the Agent account to that group:

```bash
sudo usermod -aG docker missioncontrol-agent
```

Treat Docker-socket membership as privileged host access.

The Agent stores one latest snapshot per node rather than a metrics history. `GET /api/snapshot` returns that snapshot with age, staleness, Docker availability, publication status, and sanitized protocol diagnostics. The endpoint applies configured CORS origins and a fixed request rate limit. Raw exception details and local Docker socket paths are not exposed through protocol diagnostics.

## Web Dashboard behavior

The Dashboard requires an authenticated user and provides:

- **Home**: Archive totals/categories and a compact live summary for every configured Agent node;
- **Hosts**: per-node health, CPU/memory, container/protocol counts, publication status, and GreenCloud bandwidth where configured;
- **Services**: a host selector and node-owned catalog entries correlated with that node's containers and protocol probes, including uncatalogued observations;
- **Events**: filtering, cursor-based older-event loading, modal/full-page details, and periodic checks for new events;
- **Git Activity**: a bounded recent-commit feed with repository and branch filters;
- **Work**: daily/random picks, work-item summaries, and quick todo creation through the Work Planning integration.

Home, Hosts, and Services refresh Agent fleet state around `Dashboard:Refresh:AgentSnapshotRefreshSeconds` (30 seconds by default). Node failures are isolated, and the UI keeps last-known snapshots where possible while marking errors and stale data. Event polling similarly preserves the user's position when older events are loaded.

The service catalog is loaded from `MissionControl.Dashboard/services.json` and reloads automatically. Each definition's `NodeId` owns that service. `ContainerName` and `ProtocolServiceKey` are matched only against the selected node's snapshot, so the same container or probe name may exist on different nodes. An invalid catalog reload keeps the last valid catalog visible and displays a warning.

GreenCloud bandwidth uses a process-wide Dashboard cache. A hosted service refreshes configured servers on startup and then according to `GreenCloud:PollSeconds` (300 seconds by default). Hosts and Mobile fleet responses read that cache; they do not initiate a fleet provider refresh. Provider failures preserve last-known bandwidth when available and do not block otherwise healthy Agent telemetry.

Dashboard authentication uses a local SQLite user database and persisted ASP.NET Core Data Protection keys.

## Mobile client behavior

`MissionControl.Mobile` is one .NET MAUI Blazor Hybrid application. The current
project builds Android on all supported build hosts, adds iOS and Mac Catalyst
targets on non-Linux hosts, and adds the Windows target on Windows. The installed
applications share Razor UI, the Mission Control theme, contracts, and typed
HTTP clients.

The mobile app currently provides:

- **Home**: Archive statistics and a compact summary of all configured hosts;
- **Hosts**: per-node resources, status, container/protocol counts, and cached bandwidth;
- **Services**: a host selector and bundled node-aware service catalog correlated with fleet telemetry;
- **Events**: source and event-type filters, cursor-based older-event loading,
  manual refresh, new-event detection, event cards, modal details, and a
  full-page details route;
- **Git Activity**: the same shared bounded recent-commit feed, loaded through the
  authenticated Dashboard Mobile API rather than the private service;
- **Work**: Work Planning picks, work-item summaries, and todo creation;
- **Settings**: entry, testing, storage, and removal of the raw Mobile API
  bearer token.

All server data flows through the bearer-authenticated Dashboard Mobile API. The app stores only the raw Dashboard token in MAUI `SecureStorage`; it does not receive private Agent, Archive, GitActivity, GreenCloud, or Work Planning credentials or URLs. Host-related pages poll `GET /api/mobile/hosts` around every 30 seconds and support manual refresh. Those requests refresh Agent fleet state but consume the latest cached GreenCloud state.

The app reads the shared service catalog from its bundled `services.json` asset, included from `MissionControl.Dashboard/services.json` at build time. Dashboard catalog changes require a Mobile rebuild before they appear in the installed app.

## Requirements

For building and automated testing:

- .NET 10 SDK
- a supported .NET development platform

The test suite uses temporary SQLite databases and controlled HTTP, NATS, Docker, and protocol doubles. It does not require a running NATS server, Docker daemon, external network service, or production credentials.

For a complete local event flow:

- NATS with JetStream enabled, reachable by Gateway, Archive, and GitActivity;
- writable storage for Archive, Agent, Dashboard authentication, and GitActivity SQLite databases;
- valid API keys and, when enabled, a GitHub webhook secret;
- Archive and every configured Agent HTTP endpoint reachable by Dashboard.

For full Agent metrics, run on Linux with permission to read `/proc` and access the configured Docker Unix socket. Protocol probes additionally require network access to their configured targets.

## Configuration

All executables use standard ASP.NET Core configuration. Environment variables replace `:` with `__`; for example, `Agent__PublicationHeartbeatMinutes=5` or `EventArchive__DatabaseFileName=mission-control.db`. Keep credentials in user secrets, environment variables, or another secret provider rather than checked-in JSON.

### Gateway

Important sections are `EventSources`, `Nats`, and `GitHubWebhook`.

```json
{
  "EventSources": {
    "Sources": [
      {
        "Name": "example-source",
        "ApiKey": "replace-with-at-least-32-characters"
      }
    ]
  },
  "Nats": {
    "Url": "nats://localhost:4222",
    "ClientName": "mission-control-gateway-local",
    "StreamName": "MISSION_CONTROL_EVENTS"
  },
  "GitHubWebhook": {
    "Enabled": true,
    "Secret": "replace-with-at-least-32-characters",
    "AllowedOwner": "example-owner",
    "MaxPayloadBytes": 5242880
  }
}
```

Event-source names and keys must be non-empty and unique; keys must contain at least 32 characters. An enabled GitHub webhook requires a secret of at least 32 characters and an allowed owner. Payload limits must be between 1 byte and 25 MB.

`Nats:Url` must use the `nats` or `tls` scheme. `Nats:ClientName` and
`Nats:StreamName` are required. The checked-in application settings use
`MISSION_CONTROL_EVENTS`; all publishers and consumers must point at the same
stream.

### Archive

Archive requires `Nats`, `NatsConsumer`, and `EventArchive` settings.

```json
{
  "Nats": {
    "Url": "nats://localhost:4222",
    "ClientName": "mission-control-archive-local",
    "StreamName": "MISSION_CONTROL_EVENTS"
  },
  "NatsConsumer": {
    "DurableName": "mission-control-archive",
    "FilterSubject": "events.>",
    "MaxDeliveries": 5
  },
  "EventArchive": {
    "DatabaseFileName": "mission-control.db",
    "BasePath": "Data"
  }
}
```

`EventArchive` and `DatabaseFileName` are required and validated before the NATS consumer starts. `DatabaseFileName` must be a filename, not a path. A missing or blank `BasePath` resolves to `Data` under the application directory; a relative value is also resolved from the application directory, while an absolute value is used directly. Invalid paths fail startup instead of falling back to another database. `NatsConsumer:DurableName` and `NatsConsumer:FilterSubject` are required, and `MaxDeliveries` must be greater than zero.

### Agent

Agent configuration is split across `Agent`, `AgentApi`, `AgentStorage`, and `MissionControl`.

```json
{
  "Agent": {
    "NodeId": "example-node",
    "NodeName": "local-node",
    "IntervalSeconds": 60,
    "PublicationHeartbeatMinutes": 15,
    "DockerEnabled": true,
    "DockerSocketPath": "/var/run/docker.sock",
    "DockerTimeoutSeconds": 5,
    "Probes": [
      {
        "Name": "example-echo",
        "Host": "127.0.0.1",
        "Protocol": "echo",
        "Port": 7,
        "TimeoutMilliseconds": 2000
      }
    ]
  },
  "AgentApi": {
    "StaleAfterSeconds": 180,
    "AllowedOrigins": [
      "https://dashboard.example.com"
    ]
  },
  "AgentStorage": {
    "DatabaseFileName": "mission-control-agent.db",
    "BasePath": "Data"
  },
  "MissionControl": {
    "Enabled": false,
    "BaseUrl": "http://127.0.0.1:5190",
    "ApiKey": "",
    "TimeoutMilliseconds": 1000
  }
}
```

`Agent:NodeId` is the stable fleet identity; `NodeName` is the display name and is used as a compatibility fallback when `NodeId` is absent. Keep the Agent ID aligned with Dashboard `Agents:Nodes[].NodeId` and service-catalog ownership. Collection interval, publication heartbeat, Docker timeout, and probe timeouts must be positive. Probe ports must be between 1 and 65535. `MissionControl.Enabled` controls publication to the configured Mission Control destination; local snapshot persistence and the Agent API continue independently of publication suppression.

### Dashboard

Dashboard is the server-side integration host. Its main sections are `Archive`, `Agents`, `Dashboard`, `GitActivityApi`, `GreenCloud`, `MissionControl`, `WorkPlanningApi`, and the `ServiceCatalog` supplied by `services.json`.

```json
{
  "Archive": {
    "BaseUrl": "http://localhost:5191/"
  },
  "Agent": {
    "BaseUrl": "http://localhost:5194/"
  },
  "Agents": {
    "Nodes": [
      {
        "NodeId": "clanker",
        "DisplayName": "Clanker",
        "BaseUrl": "http://localhost:5194/"
      },
      {
        "NodeId": "scopecreep",
        "DisplayName": "ScopeCreep",
        "BaseUrl": "http://localhost:5195/"
      }
    ]
  },
  "GitActivityApi": {
    "Enabled": true,
    "BaseUrl": "http://gitactivity:8080/",
    "ApiKey": "replace-with-the-private-service-key"
  },
  "WorkPlanningApi": {
    "BaseUrl": "http://localhost:5095/",
    "ApiKey": "replace-with-the-private-work-planning-key"
  },
  "GreenCloud": {
    "Enabled": true,
    "BaseUrl": "https://cp.green.cloud/",
    "ApiToken": "replace-with-the-provider-token",
    "Servers": [
      {
        "NodeId": "clanker",
        "ServerId": "provider-server-id"
      }
    ],
    "PollSeconds": 300
  },
  "Dashboard": {
    "Refresh": {
      "AgentSnapshotRefreshSeconds": 30,
      "EventRefreshSeconds": 30,
      "SnapshotStaleAfterSeconds": 120
    },
    "DateTime": {
      "TimeZoneId": "UTC",
      "Format": "yyyy-MM-dd HH:mm:ss zzz"
    },
    "Authentication": {
      "DatabaseFileName": "dashboard-auth.db",
      "BasePath": "data",
      "DataProtectionKeysPath": "data/data-protection",
      "CookieLifetimeHours": 8,
      "MaxFailedAttempts": 5,
      "LockoutMinutes": 15
    },
    "MobileApi": {
      "Enabled": true,
      "TokenHash": "base64-encoded-sha256-token-hash"
    }
  },
  "MissionControl": {
    "Enabled": false,
    "BaseUrl": "http://127.0.0.1:5190",
    "ApiKey": "",
    "TimeoutMilliseconds": 1000
  }
}
```

`Agents:Nodes` defines the active fleet. Node IDs must be unique case-insensitively, and each node needs an absolute HTTP(S) Agent URL. The retained `Agent:BaseUrl` setting supports the older single-Agent client registration; Home, Hosts, Services, and Mobile fleet telemetry use `Agents:Nodes`.

Refresh intervals must be between 5 and 3600 seconds; the stale threshold must be between 5 and 86400 seconds. `ServiceCatalog:Services` is supplied by `services.json` and must contain at least one service. Each service's `NodeId` selects its owner node before `ContainerName` and `ProtocolServiceKey` are matched against that node's snapshot. Service IDs are globally unique; container names and protocol keys are unique per node.

When GreenCloud is enabled, `Servers[]` maps provider server IDs to Agent `NodeId` values. Dashboard refreshes the fleet cache at `GreenCloud:PollSeconds`, which must be between 60 and 3600 seconds. Dashboard and Mobile host views read the cache; they do not trigger a provider fleet request. `GreenCloud:ApiToken` is required when enabled and belongs in secrets, not checked-in JSON. `GreenCloud:ServerId` and `GET /api/mobile/bandwidth` remain a single-server compatibility path; current multi-node host presentation uses `Servers[]` and `GET /api/mobile/hosts`.

`WorkPlanningApi` configures Dashboard's server-side Work Planning client. Its bearer key stays in Dashboard and Mobile uses the authenticated Dashboard proxy.

`Dashboard:MobileApi:Enabled` controls the bearer-authenticated Mobile API used
by installed Windows and Android clients. `Dashboard:MobileApi:TokenHash` is the
Base64-encoded SHA-256 hash of the expected token, not the raw token. Enter the
raw token independently in each installed app's Settings page; it is stored with
MAUI `SecureStorage`. Keep the Dashboard `Archive:BaseUrl` pointed at an
internal Archive URL.

`GitActivityApi:BaseUrl` must be an absolute HTTP or HTTPS URL.
`GitActivityApi:ApiKey` is required when the integration is enabled and must
match `GitActivity:ApiKey` on the private service. In container deployments,
configure these as `GitActivityApi__BaseUrl`, `GitActivityApi__Enabled`,
`GitActivityApi__ApiKey`, and `GitActivity__ApiKey`. Dashboard and Mobile do not
require public GitActivity exposure. If the API is separately exposed for a
trusted server-side integration, retain its API-key authentication and do not
ship that key to browser or Mobile clients.

One way to derive the placeholder hash locally with PowerShell is:

```powershell
$token = Read-Host -AsSecureString "Mobile API token"
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($token)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    [Convert]::ToBase64String(
        [Security.Cryptography.SHA256]::HashData(
            [Text.Encoding]::UTF8.GetBytes($plain)))
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
}
```

The optional Mission Control client publishes successful and failed Dashboard login events when enabled. Create a local Dashboard user from an interactive terminal with:

```bash
dotnet run --project MissionControl.Dashboard -- users create operator "Local Operator"
```

### GitActivity

GitActivity uses the shared `Nats` and `NatsConsumer` sections plus `GitActivity`.

```json
{
  "Nats": {
    "Url": "nats://localhost:4222",
    "ClientName": "mission-control-git-activity-local",
    "StreamName": "MISSION_CONTROL_EVENTS"
  },
  "NatsConsumer": {
    "DurableName": "mission-control-git-activity",
    "FilterSubject": "events.github.push.received",
    "MaxDeliveries": 5
  },
  "GitActivity": {
    "DatabaseFileName": "git-activity.db",
    "BasePath": "Data",
    "DefaultResultLimit": 10,
    "MaxResultLimit": 50,
    "ApiKey": "replace-with-at-least-32-characters",
    "AllowedRepositories": [
      "example-owner/example-repository"
    ],
    "AllowedBranches": [
      "main"
    ]
  }
}
```

The API key must contain at least 32 characters. Both allowlists must be non-empty, and the default result limit cannot exceed the maximum.

## HTTP endpoints

| Service | Endpoint | Notes |
| --- | --- | --- |
| Gateway | `POST /api/events` | Requires one configured `X-Mission-Control-Key`; accepts a generic integration event. |
| Gateway | `POST /api/webhooks/github` | Requires GitHub event/delivery headers and a valid `X-Hub-Signature-256` when enabled. |
| Archive | `GET /api/events` | Recent complete archived events; supports limit/source/type/before filtering. |
| Archive | `GET /api/events/feed` | Summary feed with a stable three-part cursor. |
| Archive | `GET /api/events/{eventId}` | Complete event metadata and payload, or 404. |
| Archive | `GET /api/events/statistics` | Archive totals, 24-hour counts, and top categories. |
| Dashboard Mobile API | `GET /api/mobile/ping` | Requires `Authorization: Bearer <raw-token>` and validates the configured `Dashboard:MobileApi:TokenHash`. Used by installed clients to test Settings. |
| Dashboard Mobile API | `GET /api/events/feed` | Requires the Mobile API bearer token. Proxies to the internal Archive client; supports limit/source/eventType and the three cursor fields. |
| Dashboard Mobile API | `GET /api/events/statistics` | Requires the Mobile API bearer token. Proxies Archive statistics through Dashboard. |
| Dashboard Mobile API | `GET /api/events/{eventId}` | Requires the Mobile API bearer token. Proxies complete Archive event details through Dashboard. |
| Dashboard Mobile API | `GET /api/mobile/git-activity` | Requires the Mobile API bearer token. Returns a bounded recent feed through Dashboard's private GitActivity client with no-store headers. |
| Dashboard Mobile API | `GET /api/mobile/hosts` | Requires the Mobile API bearer token. Refreshes configured Agents and combines them with the latest cached GreenCloud fleet state. |
| Dashboard Mobile API | `GET /api/mobile/work-planning/*` | Requires the Mobile API bearer token. Proxies Work Planning reads and todo creation with Dashboard's server credential. |
| Dashboard Mobile API | `GET /api/mobile/bandwidth` | Retained authenticated single-server compatibility endpoint; unlike the host-fleet path, it calls GreenCloud directly. |
| Agent | `GET /api/snapshot` | Latest sanitized node snapshot; returns 503 until one is stored. |
| GitActivity | `GET /api/github/activity` | Recent allowed activity; requires `X-Mission-Control-Key`. |

Gateway, Archive, and GitActivity expose `GET /health/live` and `GET /health/ready`. Gateway readiness verifies that the configured JetStream stream is available. Archive and GitActivity readiness additionally verifies that their durable NATS consumer is running and their SQLite database is healthy. Agent exposes `GET /health/live`. Dashboard pages are cookie-authenticated and redirect anonymous users to `/login`.

Dashboard pages (`/`, `/hosts`, `/services`, `/events`, `/events/{eventId}`,
`/gitactivity`, `/work`, `/login`, and `/logout`) use normal cookie authentication. The Dashboard Mobile API uses
bearer authentication and does not use the Dashboard cookie.

Archive query endpoints and Agent liveness do not add application-level
authentication. Place internal services behind appropriate network controls or
an authenticated proxy when they are not intended to be public. The Mobile API
exists so installed clients can reach fleet and integration data through
Dashboard without exposing those internal services or credentials.

## Local development

Restore, build, and test the solution:

```bash
dotnet restore MissionControl.slnx
dotnet build MissionControl.slnx --configuration Debug
dotnet test MissionControl.slnx --configuration Debug --no-build
```

For a local end-to-end event flow, start NATS with JetStream and persistent
storage:

```bash
docker run --name mission-control-nats --rm \
  -p 4222:4222 \
  -v mission-control-nats-data:/data/jetstream \
  nats:2.14.3-alpine -js -sd /data/jetstream
```

The checked-in settings use `nats://localhost:4222`. The first Gateway,
Archive, or GitActivity process to connect creates or updates the stream; no
separate stream-provisioning command is required.

Build the MAUI client on Windows for the checked-in Windows target:

```powershell
.\scripts\Build-MissionControlWindows.ps1
```

The script builds `Release` for `win-x64` by default and does not publish,
launch, or install the app. Use `-Configuration Debug` for a development build,
`-RuntimeIdentifier win-arm64` for Windows on ARM64, or `-NoRestore` when the
required assets have already been restored.

Build the MAUI client for Android:

```powershell
dotnet build .\MissionControl.Mobile\MissionControl.Mobile.csproj `
    -f net10.0-android `
    -c Debug
```

After supplying valid local configuration, run each executable in a separate terminal as needed:

```bash
dotnet run --project MissionControl.Gateway
dotnet run --project MissionControl.Archive -- --urls http://localhost:5191
dotnet run --project MissionControl.Agent
dotnet run --project MissionControl.Dashboard
dotnet run --project MissionControl.GitActivity
```

Checked-in launch profiles use port 5190 for Gateway, 5194 for Agent, 5089/7062 for Dashboard, and 5242 for GitActivity. The explicit Archive command above matches the Dashboard’s checked-in Archive URL. Gateway, Archive, and GitActivity will not be ready until NATS JetStream is available at the configured URL.

## Publishing for personal use

`MissionControl.Mobile` currently uses:

- `ApplicationTitle`: `Mission Control`
- `ApplicationId`: `com.kgivler.missioncontrol.mobile`
- `ApplicationDisplayVersion`: `1.0.2`
- `ApplicationVersion`: `3`
- Android Release package format: `apk`
- Windows package type: `None`

### Windows unpackaged publish

For a private Windows install, publish an unpackaged, self-contained, win-x64
build:

```powershell
dotnet publish .\MissionControl.Mobile\MissionControl.Mobile.csproj `
    -f net10.0-windows10.0.19041.0 `
    -c Release `
    /p:RuntimeIdentifierOverride=win-x64 `
    /p:SelfContained=true `
    /p:WindowsPackageType=None `
    /p:WindowsAppSDKSelfContained=true
```

Copy the entire publish directory to the target machine. The executable depends
on the other files in that directory; copying only the `.exe` is not enough.

### Android private APK publish

#### Automated phone install or update

The repository includes `scripts\Update-MissionControlPhone.ps1` for installing
Mission Control on a new Android phone or updating an existing installation. It
requires an authorized ADB device and an existing signing keystore. The script
does not create the keystore.

Create the default keystore once, using the same password for the keystore and
key when prompted:

```powershell
New-Item -ItemType Directory -Force "$HOME\.missioncontrol"

keytool -genkeypair `
    -keystore "$HOME\.missioncontrol\missioncontrol.keystore" `
    -alias missioncontrol `
    -keyalg RSA `
    -keysize 2048 `
    -validity 10000
```

Back up the keystore and password securely. Android requires every future
update to use the same signing key; losing it prevents updates to existing
installations.

Enable USB debugging on the phone, connect and unlock it, approve the
authorization prompt, then run this command from the repository root:

```powershell
.\scripts\Update-MissionControlPhone.ps1
```

The script increments the display and build versions, publishes a signed
Release APK, and runs `adb install -r`. That ADB command performs a first-time
install when the package is absent and preserves app data and `SecureStorage`
when updating an installation signed with the same key. The project version is
retained only after a successful installation.

If ADB is installed in the current Android SDK user directory, specify it
explicitly:

```powershell
.\scripts\Update-MissionControlPhone.ps1 `
    -AdbPath "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
```

Use `-Version 1.1.0` to choose the next display version, or `-DeviceSerial` to
select a phone when multiple ADB devices are connected. `-SkipVersionBump`
rebuilds the current project version and may be rejected by Android if the
installed build number is newer.

#### Manual APK publish

For private Android sideloading, publish a signed Release APK. Keep the keystore
file outside the repository and provide passwords through environment variables:

```powershell
$env:MC_ANDROID_KEYSTORE = "C:\path\to\mission-control-upload.keystore"
$env:MC_ANDROID_KEY_ALIAS = "mission-control-upload"
$env:MC_ANDROID_STORE_PASS = "<store-password-from-secret-manager>"
$env:MC_ANDROID_KEY_PASS = "<key-password-from-secret-manager>"

dotnet publish .\MissionControl.Mobile\MissionControl.Mobile.csproj `
    -f net10.0-android `
    -c Release `
    /p:AndroidKeyStore=true `
    /p:AndroidSigningKeyStore="$env:MC_ANDROID_KEYSTORE" `
    /p:AndroidSigningKeyAlias="$env:MC_ANDROID_KEY_ALIAS" `
    /p:AndroidSigningStorePass="$env:MC_ANDROID_STORE_PASS" `
    /p:AndroidSigningKeyPass="$env:MC_ANDROID_KEY_PASS"
```

Install or update the APK with:

```powershell
adb install -r .\path\to\com.kgivler.missioncontrol.mobile-Signed.apk
```

Retain the same keystore and alias for future updates. Never commit the
keystore, signing passwords, or release credentials. Google Play/AAB publishing
is not the current release path documented here.

For releases, `ApplicationDisplayVersion` is the user-facing version,
`ApplicationVersion` must increase, and `ApplicationId` must remain stable.
Android updates must be signed with the same signing key.

## Installing a Linux Agent on a host

Current production-style Linux Agent deployments run the published
`MissionControl.Agent` directly under systemd so host metrics come from the
host and Docker can be inspected through the local Unix socket.

The recommended Linux deployment is a **self-contained `linux-x64` publish**.
The build itself can run inside the .NET SDK container, so the target host does
not need the .NET runtime or SDK installed. Do not use the framework-dependent
publish command for hosts without .NET; the apphost will start and immediately
fail with `You must install .NET to run this application`.

Build a writable source copy and publish the Agent:

```bash
rm -rf /tmp/missioncontrol-build /tmp/missioncontrol-agent-publish
mkdir -p /tmp/missioncontrol-build /tmp/missioncontrol-agent-publish
cp -a . /tmp/missioncontrol-build/src

docker run --rm \
  -v /tmp/missioncontrol-build/src:/src \
  -v /tmp/missioncontrol-agent-publish:/out \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet publish MissionControl.Agent/MissionControl.Agent.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o /out
```

The source copy is intentionally writable because `dotnet publish` writes
restore/build intermediates under the source tree. A read-only `/src` bind
causes the container build to fail while writing `obj` files.

A self-contained publish is much larger than a framework-dependent publish
(roughly 100+ MiB for the current Agent) because it carries the .NET runtime.
It can still depend on native libraries supplied by the operating system. On
Debian 13, install ICU before starting the Agent:

```bash
sudo apt update
sudo apt install -y libicu76
```

If startup logs report `Couldn't find a valid ICU package installed on the
system`, install the distribution's `libicu` package rather than enabling
globalization-invariant mode unless invariant globalization is specifically
desired.

Copy the publish output to the target host, then create a dedicated service
account and persistent state directory:

```bash
sudo useradd --system \
  --home /opt/missioncontrol-agent \
  --shell /usr/sbin/nologin \
  missioncontrol-agent 2>/dev/null || true

sudo mkdir -p /opt/missioncontrol-agent
sudo mkdir -p /var/lib/missioncontrol-agent
sudo cp -a /tmp/missioncontrol-agent-publish/. /opt/missioncontrol-agent/
sudo chown -R missioncontrol-agent:missioncontrol-agent \
  /opt/missioncontrol-agent \
  /var/lib/missioncontrol-agent
```

Store deployment-specific configuration in
`/etc/missioncontrol-agent.env`. At minimum, give every fleet member an
explicit stable `Agent__NodeId`; `Agent__NodeName` is only its display name.
The NodeId must match the corresponding Dashboard `Agents:Nodes[].NodeId`.
A typical Linux fleet node looks like:

```ini
DOTNET_ENVIRONMENT=Production
DOTNET_EnableDiagnostics=0

ASPNETCORE_URLS=http://10.99.0.10:5194

Agent__NodeId=molasses
Agent__NodeName=Molasses
Agent__DockerEnabled=true
Agent__DockerSocketPath=/var/run/docker.sock
Agent__DockerTimeoutSeconds=5
Agent__IntervalSeconds=60
Agent__PublicationHeartbeatMinutes=15

AgentStorage__DatabaseFileName=mission-control-agent.db
AgentStorage__BasePath=/var/lib/missioncontrol-agent

AgentApi__StaleAfterSeconds=180

MissionControl__Enabled=true
MissionControl__BaseUrl=http://10.99.0.1:5190
MissionControl__ApiKey=<secret>
MissionControl__TimeoutMilliseconds=2000
```

Bind the Agent API only to an address that the Dashboard should reach. The
example above uses WireGuard rather than exposing the Agent publicly. Configure
CORS origins only when a browser client needs direct Agent access.

Install a systemd unit such as:

```ini
[Unit]
Description=Mission Control Agent
Wants=network-online.target docker.service
After=network-online.target docker.service

[Service]
Type=simple
User=missioncontrol-agent
Group=missioncontrol-agent
WorkingDirectory=/opt/missioncontrol-agent
ExecStart=/opt/missioncontrol-agent/MissionControl.Agent
EnvironmentFile=/etc/missioncontrol-agent.env
Restart=always
RestartSec=5
TimeoutStopSec=30
SyslogIdentifier=missioncontrol-agent

NoNewPrivileges=true
PrivateTmp=true
ProtectHome=true
ProtectSystem=strict
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true
RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6
UMask=0027

StateDirectory=missioncontrol-agent
StateDirectoryMode=0750

[Install]
WantedBy=multi-user.target
```

Then enable and verify the service:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now missioncontrol-agent
sudo systemctl status missioncontrol-agent --no-pager
sudo journalctl -u missioncontrol-agent -n 50 --no-pager
```

A healthy Agent should expose `GET /health/live`, produce a snapshot at
`GET /api/snapshot`, report the configured NodeId, and publish successfully
when `MissionControl__Enabled=true`. The Agent SQLite database intentionally
stores only the latest snapshot for each node; historical snapshot events are
kept by the Mission Control event pipeline according to its retention policy.

## Containers

The repository contains build/deployment Dockerfiles for Gateway, Archive,
Dashboard, GitActivity, and Agent:

```bash
docker build -f Dockerfile.gateway -t mission-control-gateway .
docker build -f Dockerfile.archive -t mission-control-archive .
docker build -f Dockerfile.dashboard -t mission-control-dashboard .
docker build -f Dockerfile.gitactivity -t mission-control-gitactivity .
docker build -f Dockerfile.agent -t mission-control-agent .
```

There is no production Compose file in this repository. Production orchestration
and deployment configuration are maintained externally in
[JoyfulReaper/UsefulScripts](https://github.com/JoyfulReaper/UsefulScripts),
which is authoritative for the current production topology, images, networks,
mounts, and environment settings.

`Dockerfile.agent` makes an Agent image available as a build/deployment option;
it does not mean the current production Agents are necessarily containerized.
A containerized Agent that inspects the host Docker daemon needs access to the
configured Docker socket, which is effectively privileged host access.

Persist Archive, Agent, Dashboard authentication/Data Protection, GitActivity,
and JetStream state according to the external deployment. Supply NATS URLs,
API keys, webhook secrets, GreenCloud tokens, Work Planning keys, Mobile token
hashes, and other environment-specific values through deployment configuration
or secrets rather than baking them into images.

## Testing

The xUnit suite covers:

- generic Gateway authentication, request validation, and cancellation;
- GitHub signature, payload, owner, and normalization behavior;
- production Gateway publisher/health DI registration and NATS option validation;
- Gateway-to-Archive serialization, SQLite storage, querying, and deduplication;
- Archive section, filename, path, environment-variable, startup, and health validation;
- Agent host collection, Docker state/resource parsing, and collector-failure isolation;
- protocol probe execution, timeout, and public diagnostic sanitization;
- snapshot persistence, API/Dashboard contract compatibility, publication gating, retries, and metadata;
- multi-node fleet identity, service-catalog ownership, host aggregation, refresh, freshness, and last-known-data behavior;
- GreenCloud polling cadence, concurrency suppression, cached Mobile responses, and bandwidth-specific failure handling;
- Dashboard polling cancellation, event paging, and new-event handling;
- GitActivity contracts, private-client authentication, Mobile proxy security,
  shared feed behavior, filtering, navigation, and storage projection behavior;
- Work Planning clients and authenticated Mobile proxy behavior.

Run the repository formatting check with:

```bash
dotnet format MissionControl.slnx --verify-no-changes
```

## Security and operations

- Keep event-source keys, GitActivity and Work Planning keys, GreenCloud tokens, NATS credentials if configured, GitHub webhook secrets, and Dashboard user credentials out of source control.
- Webhook signatures and API keys are validated before event publication; GitActivity compares its API key in fixed time.
- Keep Mobile API raw tokens, token hashes, Android keystores, signing passwords, and production host secrets out of source control.
- The Dashboard stores only the configured Mobile API token hash. Installed clients store the raw token independently in MAUI `SecureStorage`.
- Mobile traffic for Archive, GitActivity, Agent fleet, GreenCloud, and Work Planning data must use the authenticated Dashboard Mobile API. MAUI must never receive upstream API keys, provider tokens, or private service URLs.
- Dashboard authentication state depends on persistent SQLite and Data Protection key storage. Back up and permission those paths appropriately.
- Access to the Docker socket is effectively privileged host access. Grant it only to a trusted Agent process.
- Agent protocol endpoints and errors are sanitized before public serialization; local collector logs can contain more operational context and should be protected accordingly.
- SQLite paths are deployment state. Mount or back up Archive, Agent, Dashboard, and GitActivity storage according to the required retention.
- JetStream storage is deployment state. Persist `/data/jetstream`, monitor the
  256 MiB stream limit, and account for the configured 7-day retention window
  when planning recovery and incident investigation.

## Current limitations

- Agent storage retains only the latest snapshot per node; it is not a historical metrics database.
- Dashboard depends on direct network access to each configured Agent for live state; archived Agent events are not the live-state source.
- Agent refresh state is page/request driven rather than a single process-wide fleet cache, so multiple Mobile clients can create additional Agent reads.
- The GreenCloud fleet cache is process-local and starts empty after a Dashboard restart. The retained single-server `/api/mobile/bandwidth` compatibility route still calls the provider directly.
- Dashboard reloads `services.json` at runtime, while Mobile receives its catalog at build time.
- Linux `/proc` supplies the implemented host CPU, load-average, and memory metrics, and Docker collection currently targets a Unix socket.
- Host uptime is not collected by the Agent.
- The repository does not include its externally maintained production Compose orchestration. `Dockerfile.agent` is available, but production Agents are not necessarily containerized.
- Automated tests avoid external infrastructure; a real Gateway → NATS JetStream → Archive/GitActivity smoke test is still recommended for deployment validation.
