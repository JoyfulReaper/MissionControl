# Mission Control

Mission Control is a small event-ingestion system for normalizing integration events, publishing them through RabbitMQ, and archiving them to SQLite for later querying.

The solution currently contains:

- `MissionControl.Gateway`: ASP.NET Core service that accepts generic integration events and GitHub App webhooks.
- `MissionControl.Archive`: ASP.NET Core service that consumes integration events from RabbitMQ and stores them in SQLite.
- `MissionControl.Contracts`: shared event request and envelope contracts.
- `MissionControl.Observability`: shared RabbitMQ readiness health-check support.
- `MissionControl.Tests`: xUnit coverage for gateway authentication, GitHub webhook normalization, event publishing behavior, and Gateway-to-Archive serialization/storage compatibility.

## Requirements

- .NET 10 SDK
- RabbitMQ for local end-to-end service runs
- SQLite is used by the Archive service and test suite

The automated tests do not require Docker, RabbitMQ, network access, user secrets, or machine-specific configuration.

## Configuration

Configuration is supplied through `appsettings.json`, user secrets, environment variables, or any standard ASP.NET Core configuration provider.

### Gateway

`MissionControl.Gateway` requires RabbitMQ settings and at least one event source API key.

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
    "Password": "replace-me",
    "VirtualHost": "/mission-control"
  },
  "GitHubWebhook": {
    "Enabled": true,
    "Secret": "replace-with-at-least-32-characters",
    "AllowedOwner": "JoyfulReaper",
    "MaxPayloadBytes": 5242880
  }
}
```

Generic events are posted to `POST /api/events` with the configured `X-Mission-Control-Key` header.

GitHub webhooks are posted to `POST /api/webhooks/github` and must include GitHub's `X-Hub-Signature-256`, `X-GitHub-Event`, and `X-GitHub-Delivery` headers.

### Archive

`MissionControl.Archive` requires RabbitMQ settings and SQLite archive settings.

```json
{
  "RabbitMq": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "mission-control",
    "Password": "replace-me",
    "VirtualHost": "/mission-control"
  },
  "EventArchive": {
    "DatabaseFileName": "mission-control.db"
  }
}
```

Archived events can be queried from `GET /api/events`.

## Local Development

Restore, build, and test the full solution:

```bash
dotnet restore MissionControl.slnx
dotnet build MissionControl.slnx -c Release
dotnet test MissionControl.slnx -c Release --no-build
```

Run the Gateway:

```bash
dotnet run --project MissionControl.Gateway
```

Run the Archive:

```bash
dotnet run --project MissionControl.Archive
```

Health endpoints:

- `GET /health/live`
- `GET /health/ready`

## Event Flow

1. The Gateway validates an incoming event source API key or GitHub webhook signature.
2. The Gateway creates an `IntegrationEventEnvelope`.
3. The Gateway publishes the envelope to RabbitMQ.
4. The Archive consumes the envelope from RabbitMQ.
5. The Archive stores the event in SQLite using `EventId` for deduplication.
6. Consumers can query archived events through the Archive API.

## Verification Status

The automated test suite covers the Gateway webhook and event-source authentication paths, malformed GitHub payload handling, generic event publishing, API-key resolution, and serialization compatibility between Gateway envelopes and Archive storage.

A manual `Gateway -> RabbitMQ -> Archive -> SQLite` smoke test is still recommended before deployment.
