using MissionControl.GitActivity;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services
    .AddOptions<GitActivityOptions>()
    .BindConfiguration(GitActivityOptions.SectionName)
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(options.DatabaseFileName),
        "GitActivity:DatabaseFileName is required.")
    .Validate(
        options => options.DefaultResultLimit > 0,
        "GitActivity:DefaultResultLimit must be greater than zero.")
    .Validate(
        options => options.MaxResultLimit > 0,
        "GitActivity:MaxResultLimit must be greater than zero.")
    .Validate(
        options =>
            options.DefaultResultLimit <= options.MaxResultLimit,
        "GitActivity:DefaultResultLimit must not exceed MaxResultLimit.")
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(options.ApiKey) &&
            options.ApiKey.Length >= 32,
        "GitActivity:ApiKey must contain at least 32 characters.")
    .Validate(
        options => options.AllowedRepositories.Length > 0,
        "GitActivity:AllowedRepositories must contain at least one repository.")
    .Validate(
        options => options.AllowedBranches.Length > 0,
        "GitActivity:AllowedBranches must contain at least one branch.")
    .ValidateOnStart();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


app.MapGet("/", () => "Mission Control Git Activity");

app.Run();

public partial class Program;