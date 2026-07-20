namespace MissionControl.UI.Components.Services;

public static class PublicationStatusPresentation
{
    public const string AttemptTimestampLabel =
        "Last publish attempt";

    public static string GetLabel(bool? succeeded)
    {
        return succeeded switch
        {
            true => "LAST ATTEMPT SUCCEEDED",
            false => "LAST ATTEMPT FAILED",
            _ => "NO PUBLISH ATTEMPT RECORDED"
        };
    }

    public static string GetCssClass(bool? succeeded)
    {
        return succeeded switch
        {
            true => "status-running",
            false => "status-stopped",
            _ => "status-unknown"
        };
    }
}
