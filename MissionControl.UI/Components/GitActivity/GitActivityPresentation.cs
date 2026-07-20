using MissionControl.Contracts.GitActivity;

namespace MissionControl.UI.Components.GitActivity;

internal static class GitActivityPresentation
{
    public static string AbbreviateSha(string sha)
    {
        return sha.Length <= 8
            ? sha
            : sha[..8];
    }

    public static string FormatAuthor(GitActivityItem item)
    {
        bool hasAuthor = !string.IsNullOrWhiteSpace(item.Author);
        bool hasUsername =
            !string.IsNullOrWhiteSpace(item.AuthorUsername);

        return (hasAuthor, hasUsername) switch
        {
            (true, true) =>
                $"{item.Author} (@{item.AuthorUsername})",
            (true, false) => item.Author!,
            (false, true) => $"@{item.AuthorUsername}",
            _ => "Unknown author"
        };
    }

    public static string? GetSafeCommitUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
               uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri
            : null;
    }

    public static string GetItemKey(GitActivityItem item)
    {
        return $"{item.Repository}\n{item.Branch}\n{item.Sha}";
    }
}
