using MissionControl.Contracts;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


app.MapPost("/api/events", (
    PublishEventRequest request,
    ILogger<Program> logger) =>
    {
        var errors = new Dictionary<string, string[]>();
        if (request.EventId == Guid.Empty)
        {
            errors[nameof(request.EventId)] = ["EventId must not be empty"];
        }

        if (string.IsNullOrWhiteSpace(request.EventType))
        {
            errors[nameof(request.EventType)] =
                ["EventType is required."];
        }

        if (request.SchemaVersion < 1)
        {
            errors[nameof(request.SchemaVersion)] =
                ["SchemaVersion must be at least 1."];
        }

        if (request.OccurredAt == default)
        {
            errors[nameof(request.OccurredAt)] =
                ["OccurredAt is required."];
        }

        if (request.Payload.ValueKind != JsonValueKind.Object)
        {
            errors[nameof(request.Payload)] =
                ["Payload must be a JSON object."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        logger.LogDebug(
            "Received event {EventType} with ID {EventId}, version {SchemaVersion}, occurring at {OccurredAt}",
            request.EventType,
            request.EventId,
            request.SchemaVersion,
            request.OccurredAt);

        return Results.Accepted(value: new PublishEventAcceptedResponse(request.EventId));
    }
)
.WithName("PublishEvent")
.WithTags("Events")
.Produces<PublishEventAcceptedResponse>(
        StatusCodes.Status202Accepted)
    .ProducesValidationProblem();

app.Run();
