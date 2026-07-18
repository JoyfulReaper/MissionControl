# Mission Control

Mission Control is a .NET operations system for collecting integration events and current host/service status. It accepts authenticated events, routes them through RabbitMQ, archives complete event envelopes in SQLite, projects selected GitHub activity, collects host and Docker diagnostics, and presents the results in an authenticated Blazor dashboard.

## Solution projects

| Project | Purpose |
| --- | --- |
| `MissionControl.Gateway` | ASP.NET Core ingress for generic integration events and signed GitHub webhooks. Publishes normalized envelopes to RabbitMQ. |
| `MissionControl.Archive` | RabbitMQ consumer and HTTP query API backed by a SQLite event archive. |
| `MissionControl.Agent` | Collects host, Docker, and protocol status; persists the latest node snapshot; optionally publishes operational snapshot events; exposes a sanitized snapshot API. |
| `MissionControl.Dashboard` | Authenticated Blazor Server UI for archive statistics, events, node resources, containers, probes, and a configured service catalog. |
| `MissionControl.GitActivity` | Consumes selected GitHub push events, stores an allowed repository/branch projection in SQLite, and exposes an API-key-protected activity feed. |
| `MissionControl.Contracts` | Shared integration-event and GitHub payload contracts. |
| `MissionControl.Messaging.RabbitMq` | Shared RabbitMQ consumer, consumer options, and integration-event processor contract. |
| `MissionControl.Observability` | Shared liveness/readiness endpoint mapping and RabbitMQ connection health check. |
| `MissionControl.Tests` | xUnit unit and focused integration coverage for the solution. |

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

`MissionControl.GitActivity` consumes `github.push.received` events from its own RabbitMQ queue. It filters configured repositories and branches, then stores a commit-oriented SQLite projection for `GET /api/github/activity`.

The Agent path is separate:

```text
host, Docker, and protocol collectors
-> AgentWorker
-> latest snapshot in Agent SQLite
-> GET /api/snapshot
-> Dashboard Overview and Services pages
```

Every Agent collection is saved locally. A snapshot is published as an integration event only for the first successful attempt, an operational-state change, or the configured heartbeat. CPU, memory, container resource usage, probe duration, and diagnostic wording alone do not trigger publication. A failed publication remains eligible for the next collection, and successful publication metadata is recorded without preventing later snapshot persistence.

## Agent monitoring

The Agent currently collects:

- logical processor count, CPU usage, and memory totals/availability from Linux `/proc` data;
- all Docker containers, including running, stopped, exited, created, and restarting states;
- image, restart count, and resource metrics when the Docker API supplies them;
- Echo, QOTD, Gopher, Finger, and Daytime protocol checks with per-probe timeouts.

Docker collection uses a Unix domain socket, normally `/var/run/docker.sock`. Docker is disabled by default on Windows. On non-Linux hosts the Agent reports logical processor count, but CPU and memory values are unavailable. A Docker outage or an individual container-statistics failure does not discard host and protocol results.

The Agent stores one latest snapshot per node rather than a metrics history. `GET /api/snapshot` returns that snapshot with age, staleness, Docker availability, publication status, and sanitized protocol diagnostics. The endpoint applies configured CORS origins and a fixed request rate limit. Raw exception details and local Docker socket paths are not exposed through protocol diagnostics.

## Dashboard behavior

The Dashboard requires an authenticated user and provides:

- **Overview**: archive totals and categories plus the current node CPU/memory snapshot;
- **Services**: service-catalog entries correlated with Agent containers and protocol probes, including uncatalogued observations;
- **Events**: filtering, cursor-based older-event loading, modal/full-page details, and periodic checks for new events.

Agent data on Overview and Services refreshes automatically. Event data also polls automatically. Freshness is recalculated locally between requests. When a later refresh fails, the last successful Agent or event data remains visible with a warning. If older events are loaded, polling preserves the current list and shows a “new events available” action instead of replacing the user’s position.

The service catalog is loaded from the required `MissionControl.Dashboard/services.json` file and reloads automatically. An invalid reload keeps the last valid catalog visible and displays a warning until a later valid update succeeds. Dashboard authentication uses a local SQLite user database and persisted ASP.NET Core Data Protection keys.

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

The optional Mission Control client publishes successful Dashboard login events when enabled. Create a local Dashboard user from an interactive terminal with:

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
| Agent | `GET /api/snapshot` | Latest sanitized node snapshot; returns 503 until one is stored. |
| GitActivity | `GET /api/github/activity` | Recent allowed activity; requires `X-Mission-Control-Key`. |

Gateway, Archive, and GitActivity expose `GET /health/live` and `GET /health/ready`. Readiness includes their RabbitMQ status and, for SQLite consumers, database health. Agent exposes `GET /health/live`. Dashboard pages are cookie-authenticated and redirect anonymous users to `/login`.

Archive query endpoints and Agent liveness do not add application-level authentication. Place internal services behind appropriate network controls or an authenticated proxy when they are not intended to be public.

## Local development

Restore, build, and test the solution:

```bash
dotnet restore MissionControl.slnx
dotnet build MissionControl.slnx --configuration Debug
dotnet test MissionControl.slnx --configuration Debug --no-build
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

## Containers

The repository contains Dockerfiles for Gateway, Archive, Dashboard, and GitActivity:

```bash
docker build -f Dockerfile.gateway -t mission-control-gateway .
docker build -f Dockerfile.archive -t mission-control-archive .
docker build -f Dockerfile.dashboard -t mission-control-dashboard .
docker build -f Dockerfile.gitactivity -t mission-control-gitactivity .
```

There is no Compose file checked into this repository and no Agent Dockerfile. Production Compose orchestration is maintained externally and is not part of the MissionControl repository.

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
- Dashboard authentication state depends on persistent SQLite and Data Protection key storage. Back up and permission those paths appropriately.
- Access to the Docker socket is effectively privileged host access. Grant it only to a trusted Agent process.
- Agent protocol endpoints and errors are sanitized before public serialization; local collector logs can contain more operational context and should be protected accordingly.
- SQLite paths are deployment state. Mount or back up Archive, Agent, Dashboard, and GitActivity storage according to the required retention.

## Current limitations

- Agent storage retains only the latest snapshot per node; it is not a historical metrics database.
- Linux `/proc` supplies the implemented host CPU and memory metrics, and Docker collection currently targets a Unix socket.
- Host uptime is not collected by the Agent.
- The repository does not include its externally maintained Compose orchestration or an Agent container image.
- Automated tests avoid external infrastructure; a real Gateway → RabbitMQ → Archive/GitActivity smoke test is still recommended for deployment validation.
