# Mission Control

Mission Control is a .NET operations system for collecting integration events and current host/service status. It accepts authenticated events, routes them through RabbitMQ, archives complete event envelopes in SQLite, projects selected GitHub activity, collects host and Docker diagnostics, and presents the results in an authenticated Blazor dashboard.

## Solution projects

| Project | Purpose |
| --- | --- |
| `MissionControl.Gateway` | ASP.NET Core ingress for generic integration events and signed GitHub webhooks. Publishes normalized envelopes to RabbitMQ. |
| `MissionControl.Archive` | RabbitMQ consumer and HTTP query API backed by a SQLite event archive. |
| `MissionControl.Agent` | Collects host, Docker, and protocol status; persists the latest node snapshot; optionally publishes operational snapshot events; exposes a sanitized snapshot API. |
| `MissionControl.Dashboard` | Authenticated Blazor Server UI for archive statistics, events, node resources, containers, probes, and a configured service catalog. Also exposes the bearer-authenticated Mobile API proxy for installed clients. |
| `MissionControl.GitActivity` | Consumes selected GitHub push events, stores an allowed repository/branch projection in SQLite, and exposes an API-key-protected activity feed. |
| `MissionControl.Contracts` | Shared Agent, Archive, service-catalog, integration-event, and GitHub transport contracts. |
| `MissionControl.Client` | Shared Agent and Archive HTTP clients plus client-side event-feed state, including cursor paging and new-event detection. |
| `MissionControl.UI` | Razor Class Library with shared dashboard components, presentation helpers, event details UI, overview panels, service panels, and the shared Mission Control theme. |
| `MissionControl.Mobile` | .NET MAUI Blazor Hybrid application sharing the Client, Contracts, and UI projects across Windows and Android builds. |
| `MissionControl.Messaging.RabbitMq` | Shared RabbitMQ consumer, consumer options, and integration-event processor contract. |
| `MissionControl.Observability` | Shared liveness/readiness endpoint mapping and RabbitMQ connection health check. |
| `MissionControl.Tests` | xUnit unit and focused integration coverage for storage, messaging, Agent, Dashboard, shared client/UI behavior, GitActivity, and API contracts. |

## Data flow

The primary integration-event path is:

```text
generic client or GitHub
-> MissionControl.Gateway
-> RabbitMQ topic exchange
-> MissionControl.Archive
-> SQLite event archive
-> Archive HTTP API
-> MissionControl.Dashboard
```

The shared cross-platform UI layers sit above those service APIs:

```text
MissionControl.Contracts
-> MissionControl.Client
-> MissionControl.UI
   -> MissionControl.Dashboard authenticated Blazor web app
   -> MissionControl.Mobile Windows MAUI Blazor Hybrid app
   -> MissionControl.Mobile Android MAUI Blazor Hybrid app
```

`MissionControl.GitActivity` consumes `github.push.received` events from its own RabbitMQ queue. It filters configured repositories and branches, then stores a commit-oriented SQLite projection for `GET /api/github/activity`.

The Agent path is separate:

```text
host, Docker, and protocol collectors
-> AgentWorker
-> latest snapshot in Agent SQLite
-> GET /api/snapshot
-> Dashboard Overview and Services pages
```

Installed Windows and Android clients share the same Razor UI and service clients.
Archive data is reached through Dashboard, not by exposing Archive publicly:

```text
Windows / Android MAUI client
-> HTTPS Dashboard Mobile API with bearer token
-> internal Dashboard Archive client
-> private Archive HTTP service
```

The current mobile app also polls the configured Agent snapshot API for live
host, container, and protocol status. Keep Archive private; do not publish the
Archive HTTP service solely to support installed clients.

Every Agent collection is saved locally. A snapshot is published as an integration event only for the first successful attempt, an operational-state change, or the configured heartbeat. CPU, memory, container resource usage, probe duration, and diagnostic wording alone do not trigger publication. A failed publication remains eligible for the next collection, and successful publication metadata is recorded without preventing later snapshot persistence.

## Agent monitoring

The Agent currently collects:

- logical processor count, CPU usage, and memory totals/availability from Linux `/proc` data;
- all Docker containers, including running, stopped, exited, created, and restarting states;
- image, restart count, and resource metrics when the Docker API supplies them;
- Echo, QOTD, Gopher, Finger, and Daytime protocol checks with per-probe timeouts.

Docker collection uses a Unix domain socket, normally `/var/run/docker.sock`. Docker is disabled by default on Windows. On non-Linux hosts the Agent reports logical processor count, but CPU and memory values are unavailable. A Docker outage or an individual container-statistics failure does not discard host and protocol results.

The Agent stores one latest snapshot per node rather than a metrics history. `GET /api/snapshot` returns that snapshot with age, staleness, Docker availability, publication status, and sanitized protocol diagnostics. The endpoint applies configured CORS origins and a fixed request rate limit. Raw exception details and local Docker socket paths are not exposed through protocol diagnostics.

## Web Dashboard behavior

The Dashboard requires an authenticated user and provides:

- **Overview**: archive totals and categories plus the current node CPU/memory snapshot;
- **Services**: service-catalog entries correlated with Agent containers and protocol probes, including uncatalogued observations;
- **Events**: filtering, cursor-based older-event loading, modal/full-page details, and periodic checks for new events.

Agent data on Overview and Services refreshes automatically. Event data also polls automatically. Freshness is recalculated locally between requests. When a later refresh fails, the last successful Agent or event data remains visible with a warning. If older events are loaded, polling preserves the current list and shows a “new events available” action instead of replacing the user’s position.

The service catalog is loaded from the required `MissionControl.Dashboard/services.json` file and reloads automatically. An invalid reload keeps the last valid catalog visible and displays a warning until a later valid update succeeds. Dashboard authentication uses a local SQLite user database and persisted ASP.NET Core Data Protection keys.

## Windows and Android client behavior

`MissionControl.Mobile` is one .NET MAUI Blazor Hybrid application. The current
project builds Android on all supported build hosts, adds iOS and Mac Catalyst
targets on non-Linux hosts, and adds the Windows target on Windows. The installed
Windows and Android applications share the same Razor UI, shared theme,
contracts, and HTTP clients.

The mobile app currently provides:

- **Overview**: Archive statistics through the Dashboard Mobile API, Agent
  snapshot status, node resources, container counts, and protocol counts;
- **Services**: bundled service-catalog entries correlated with Agent
  containers and protocol probes;
- **Events**: source and event-type filters, cursor-based older-event loading,
  manual refresh, new-event detection, event cards, modal details, and a
  full-page details route;
- **Settings**: entry, testing, storage, and removal of the raw Mobile API
  bearer token.

Event cards open an in-place details modal. The modal supports the Close button,
backdrop dismissal, Escape dismissal on desktop, and a View full page action.
The app reads the shared service catalog from its bundled `services.json` asset,
which is included from `MissionControl.Dashboard/services.json` at build time.

Agent polling runs every 30 seconds from the mobile layout, and Overview and
Services also expose manual refresh for Agent data. Archive event and statistics
requests keep last-known data visible when a later refresh fails and surface the
failure as a warning or actionable Settings message.

## Requirements

For building and automated testing:

- .NET 10 SDK
- a supported .NET development platform

The test suite uses temporary SQLite databases and controlled HTTP, Docker, and protocol doubles. It does not require a running RabbitMQ broker, Docker daemon, external network service, or production credentials.

For a complete local event flow:

- RabbitMQ reachable by Gateway, Archive, and GitActivity;
- writable storage for Archive, Agent, Dashboard authentication, and GitActivity SQLite databases;
- valid API keys and, when enabled, a GitHub webhook secret;
- Archive and Agent HTTP endpoints reachable by Dashboard.

For full Agent metrics, run on Linux with permission to read `/proc` and access the configured Docker Unix socket. Protocol probes additionally require network access to their configured targets.

## Configuration

All executables use standard ASP.NET Core configuration. Environment variables replace `:` with `__`; for example, `Agent__PublicationHeartbeatMinutes=5` or `EventArchive__DatabaseFileName=mission-control.db`. Keep credentials in user secrets, environment variables, or another secret provider rather than checked-in JSON.

### Gateway

Important sections are `EventSources`, `RabbitMq`, and `GitHubWebhook`.

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
  "RabbitMq": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "mission-control",
    "Password": "replace-with-a-secret",
    "VirtualHost": "/mission-control",
    "ClientProvidedName": "mission-control-gateway-local"
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

### Archive

Archive requires `RabbitMq`, `RabbitMqConsumer`, and `EventArchive` settings.

```json
{
  "RabbitMq": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "mission-control",
    "Password": "replace-with-a-secret",
    "VirtualHost": "/mission-control",
    "ClientProvidedName": "mission-control-archive-local"
  },
  "RabbitMqConsumer": {
    "ExchangeName": "kgivler.events",
    "QueueName": "mission-control.archive",
    "RoutingKey": "#",
    "PrefetchCount": 10
  },
  "EventArchive": {
    "DatabaseFileName": "mission-control.db",
    "BasePath": "Data"
  }
}
```

`EventArchive` and `DatabaseFileName` are required and validated before the RabbitMQ consumer starts. `DatabaseFileName` must be a filename, not a path. A missing or blank `BasePath` resolves to `Data` under the application directory; a relative value is also resolved from the application directory, while an absolute value is used directly. Invalid paths fail startup instead of falling back to another database.

### Agent

Agent configuration is split across `Agent`, `AgentApi`, `AgentStorage`, and `MissionControl`.

```json
{
  "Agent": {
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

Collection interval, publication heartbeat, Docker timeout, and probe timeouts must be positive. Probe ports must be between 1 and 65535. `MissionControl.Enabled` controls publication to the configured Mission Control destination; local snapshot persistence and the Agent API continue independently of publication suppression.

### Dashboard

Dashboard uses upstream URLs plus `Dashboard`, `MissionControl`, and `ServiceCatalog` configuration.

```json
{
  "Archive": {
    "BaseUrl": "http://localhost:5191/"
  },
  "Agent": {
    "BaseUrl": "http://localhost:5194/"
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

Refresh intervals must be between 5 and 3600 seconds; the stale threshold must be between 5 and 86400 seconds. `ServiceCatalog:Services` is supplied by `services.json` and must contain at least one service. Each service may identify its corresponding `ContainerName` and `ProtocolServiceKey` so live Agent data can be correlated with catalog metadata.

`Dashboard:MobileApi:Enabled` controls the bearer-authenticated Mobile API used
by installed Windows and Android clients. `Dashboard:MobileApi:TokenHash` is the
Base64-encoded SHA-256 hash of the expected token, not the raw token. Enter the
raw token independently in each installed app's Settings page; it is stored with
MAUI `SecureStorage`. Keep the Dashboard `Archive:BaseUrl` pointed at an
internal Archive URL.

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

GitActivity uses the shared `RabbitMq` and `RabbitMqConsumer` sections plus `GitActivity`.

```json
{
  "RabbitMq": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "mission-control",
    "Password": "replace-with-a-secret",
    "VirtualHost": "/mission-control",
    "ClientProvidedName": "mission-control-git-activity-local"
  },
  "RabbitMqConsumer": {
    "ExchangeName": "kgivler.events",
    "QueueName": "mission-control.git-activity",
    "RoutingKey": "github.push.received",
    "PrefetchCount": 10
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
| Agent | `GET /api/snapshot` | Latest sanitized node snapshot; returns 503 until one is stored. |
| GitActivity | `GET /api/github/activity` | Recent allowed activity; requires `X-Mission-Control-Key`. |

Gateway, Archive, and GitActivity expose `GET /health/live` and `GET /health/ready`. Readiness includes their RabbitMQ status and, for SQLite consumers, database health. Agent exposes `GET /health/live`. Dashboard pages are cookie-authenticated and redirect anonymous users to `/login`.

Dashboard pages (`/`, `/events`, `/events/{eventId}`, `/services`, `/login`,
and `/logout`) use normal cookie authentication. The Dashboard Mobile API uses
bearer authentication and does not use the Dashboard cookie.

Archive query endpoints and Agent liveness do not add application-level
authentication. Place internal services behind appropriate network controls or
an authenticated proxy when they are not intended to be public. The Mobile API
exists so Archive can remain private while installed clients read Archive data
through Dashboard.

## Local development

Restore, build, and test the solution:

```bash
dotnet restore MissionControl.slnx
dotnet build MissionControl.slnx --configuration Debug
dotnet test MissionControl.slnx --configuration Debug --no-build
```

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

Checked-in launch profiles use port 5190 for Gateway, 5194 for Agent, 5089/7062 for Dashboard, and 5242 for GitActivity. The explicit Archive command above matches the Dashboard’s checked-in Archive URL. Services that connect to RabbitMQ will not be fully operational until the broker and credentials are available.

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

## Containers

The repository contains Dockerfiles for Gateway, Archive, Dashboard, and GitActivity:

```bash
docker build -f Dockerfile.gateway -t mission-control-gateway .
docker build -f Dockerfile.archive -t mission-control-archive .
docker build -f Dockerfile.dashboard -t mission-control-dashboard .
docker build -f Dockerfile.gitactivity -t mission-control-gitactivity .
```

There is no Compose file checked into this repository and no Agent Dockerfile. Production Compose orchestration is maintained externally and is not part of the MissionControl repository.
Currently the dockerfile can be found here: https://github.com/JoyfulReaper/UsefulScripts/blob/main/VPS/compose.yaml However please note it is not always updated quickly.

- Archive sets `EventArchive__BasePath=/app/data` and declares `/app/data` as a volume; mount persistent storage there.
- Dashboard declares `/app/data` for its authentication database and Data Protection keys.
- Gateway requires RabbitMQ, event-source, and optional webhook secrets through external configuration.
- GitActivity requires RabbitMQ, API-key, allowlist, and SQLite path configuration. Its Dockerfile does not declare a data volume, so persistent deployment storage must be arranged by the operator.
- An externally containerized Agent would require access to the configured Docker Unix socket, but this repository does not provide or endorse a Compose mounting recipe. Docker socket access is highly privileged.

## Testing

The xUnit suite covers:

- generic Gateway authentication, request validation, and cancellation;
- GitHub signature, payload, owner, and normalization behavior;
- production Gateway publisher/health DI registration and RabbitMQ option validation;
- Gateway-to-Archive serialization, SQLite storage, querying, and deduplication;
- Archive section, filename, path, environment-variable, startup, and health validation;
- Agent host collection, Docker state/resource parsing, and collector-failure isolation;
- protocol probe execution, timeout, and public diagnostic sanitization;
- snapshot persistence, API/Dashboard contract compatibility, publication gating, retries, and metadata;
- Dashboard refresh, freshness, last-known-data, polling cancellation, paging, and new-event handling;
- GitActivity authentication, filtering, and storage projection behavior.

Run the repository formatting check with:

```bash
dotnet format MissionControl.slnx --verify-no-changes
```

## Security and operations

- Keep event-source keys, GitActivity keys, RabbitMQ credentials, GitHub webhook secrets, and Dashboard user credentials out of source control.
- Webhook signatures and API keys are validated before event publication; GitActivity compares its API key in fixed time.
- Keep Mobile API raw tokens, token hashes, Android keystores, signing passwords, and production host secrets out of source control.
- The Dashboard stores only the configured Mobile API token hash. Installed clients store the raw token independently in MAUI `SecureStorage`.
- Mobile Archive traffic should follow the Dashboard Mobile API proxy path; do not expose Archive publicly merely to support Windows or Android clients.
- Dashboard authentication state depends on persistent SQLite and Data Protection key storage. Back up and permission those paths appropriately.
- Access to the Docker socket is effectively privileged host access. Grant it only to a trusted Agent process.
- Agent protocol endpoints and errors are sanitized before public serialization; local collector logs can contain more operational context and should be protected accordingly.
- SQLite paths are deployment state. Mount or back up Archive, Agent, Dashboard, and GitActivity storage according to the required retention.

## Current limitations

- Agent storage retains only the latest snapshot per node; it is not a historical metrics database.
- Linux `/proc` supplies the implemented host CPU, load-average, and memory metrics, and Docker collection currently targets a Unix socket.
- Host uptime is not collected by the Agent.
- The repository does not include its externally maintained Compose orchestration or an Agent container image.
- Automated tests avoid external infrastructure; a real Gateway → RabbitMQ → Archive/GitActivity smoke test is still recommended for deployment validation.

## Planned polish

- Reduce card padding and height at the mobile breakpoint.
- Preserve current desktop and web layouts while making those mobile-only changes.
