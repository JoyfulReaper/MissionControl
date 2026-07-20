using Microsoft.AspNetCore.Authentication;

namespace MissionControl.Dashboard.MobileApi;

public sealed class MobileApiAuthenticationOptions
    : AuthenticationSchemeOptions
{
    public const string SectionName =
        "Dashboard:MobileApi";

    public bool Enabled { get; set; }

    public string TokenHash { get; set; } =
        string.Empty;
}