using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using MissionControl.Dashboard.Archive;
using MissionControl.Dashboard.Components;
using MissionControl.Dashboard.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();

builder.Services
    .AddAuthentication(
        DashboardAuthenticationDefaults.Scheme)
    .AddScheme<
        AuthenticationSchemeOptions,
        DashboardAuthenticationHandler>(
        DashboardAuthenticationDefaults.Scheme,
        _ => { });

builder.Services
    .AddAuthorizationBuilder()
    .SetFallbackPolicy(
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());

var archiveBaseUrl =
    builder.Configuration["Archive:BaseUrl"]
    ?? throw new InvalidOperationException(
        "Archive:BaseUrl is not configured.");

builder.Services.AddHttpClient<
    IArchiveEventClient,
    ArchiveEventClient>(httpClient =>
    {
        httpClient.BaseAddress = new Uri(archiveBaseUrl);
        httpClient.Timeout = TimeSpan.FromSeconds(10);
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
