using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MissionControl.Client.WorkPlanning;

public sealed class WorkPlanningClient(
    HttpClient client,
    WorkPlanningClientOptions options) : IWorkPlanningClient
{
    private readonly string _requestPathPrefix = ValidateRequestPathPrefix(options.RequestPathPrefix);

    public async Task<DailyWorkPick?> GetDailyPickAsync(
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await client.GetAsync(BuildPath("daily-pick"), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await ReadRequiredAsync<DailyWorkPick>(response, cancellationToken);
    }

    public async Task<RandomWorkPick?> GetRandomPickAsync(
        bool favorPriority = false,
        CancellationToken cancellationToken = default)
    {
        string path = $"random-pick?favorPriority={favorPriority.ToString().ToLowerInvariant()}";

        using HttpResponseMessage response = await client.GetAsync(BuildPath(path), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await ReadRequiredAsync<RandomWorkPick>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkPlanningWorkItem>>
        GetWorkItemsAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await client.GetAsync(BuildPath("work-items"), cancellationToken);

        response.EnsureSuccessStatusCode();

        var workItems =
            await ReadRequiredAsync<
                WorkPlanningWorkItem[]>(
                response,
                cancellationToken);

        return workItems;
    }

    public async Task<WorkPlanningTodo> CreateTodoAsync(
        int workItemId,
        CreateWorkPlanningTodoRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await client.PostAsJsonAsync(
                BuildPath($"work-items/{workItemId}/todos"),
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        return await ReadRequiredAsync<WorkPlanningTodo>(
            response,
            cancellationToken);
    }

    private string BuildPath(string relativePath)
    {
        return _requestPathPrefix + relativePath;
    }

    private static string ValidateRequestPathPrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith('/') ||
            value.StartsWith('\\') ||
            !Uri.TryCreate(
                value,
                UriKind.Relative,
                out _))
        {
            throw new InvalidOperationException(
                "The Work Planning request path " +
                "prefix must be relative.");
        }

        return value.EndsWith('/') ? value : $"{value}/";
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            T? value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

            return value ??
                throw new InvalidOperationException(
                    "The Work Planning API response was empty.");
        }
        catch (JsonException exception)
        {
            string? contentType = response.Content.Headers.ContentType?.ToString();

            throw new InvalidOperationException(
                "The Work Planning API returned invalid JSON. " +
                $"Status: {(int)response.StatusCode} {response.ReasonPhrase}. " +
                $"Content-Type: {contentType ?? "unknown"}.",
                exception);
        }
    }
}