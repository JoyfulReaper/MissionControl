using System.Text.Json.Serialization;

namespace MissionControl.Gateway.Integrations.GitHub;

public sealed record GitHubPushWebhook(
    [property: JsonPropertyName("ref")]
    string Ref,

    [property: JsonPropertyName("before")]
    string Before,

    [property: JsonPropertyName("after")]
    string After,

    [property: JsonPropertyName("created")]
    bool Created,

    [property: JsonPropertyName("deleted")]
    bool Deleted,

    [property: JsonPropertyName("forced")]
    bool Forced,

    [property: JsonPropertyName("compare")]
    string? CompareUrl,

    [property: JsonPropertyName("repository")]
    GitHubWebhookRepository Repository,

    [property: JsonPropertyName("pusher")]
    GitHubWebhookPusher Pusher,

    [property: JsonPropertyName("sender")]
    GitHubWebhookSender Sender,

    [property: JsonPropertyName("commits")]
    IReadOnlyList<GitHubWebhookCommit> Commits,

    [property: JsonPropertyName("head_commit")]
    GitHubWebhookCommit? HeadCommit);

public sealed record GitHubWebhookRepository(
    [property: JsonPropertyName("id")]
    long Id,

    [property: JsonPropertyName("full_name")]
    string FullName,

    [property: JsonPropertyName("html_url")]
    string HtmlUrl,

    [property: JsonPropertyName("owner")]
    GitHubWebhookOwner Owner);

public sealed record GitHubWebhookOwner(
    [property: JsonPropertyName("login")]
    string Login);

public sealed record GitHubWebhookPusher(
    [property: JsonPropertyName("name")]
    string? Name);

public sealed record GitHubWebhookSender(
    [property: JsonPropertyName("login")]
    string Login);

public sealed record GitHubWebhookCommit(
    [property: JsonPropertyName("id")]
    string Id,

    [property: JsonPropertyName("message")]
    string Message,

    [property: JsonPropertyName("timestamp")]
    DateTimeOffset Timestamp,

    [property: JsonPropertyName("url")]
    string Url,

    [property: JsonPropertyName("author")]
    GitHubWebhookCommitAuthor? Author);

public sealed record GitHubWebhookCommitAuthor(
    [property: JsonPropertyName("name")]
    string? Name,

    [property: JsonPropertyName("username")]
    string? Username);

public sealed record GitHubPushReceivedEvent(
    string Repository,
    long RepositoryId,
    string RepositoryUrl,
    string Branch,
    string Ref,
    string Before,
    string After,
    bool Created,
    bool Forced,
    string? Pusher,
    string Sender,
    string? CompareUrl,
    int CommitCount,
    IReadOnlyList<GitHubCommitSummary> Commits);

public sealed record GitHubCommitSummary(
    string Sha,
    string Message,
    string? Author,
    string? AuthorUsername,
    DateTimeOffset Timestamp,
    string Url);