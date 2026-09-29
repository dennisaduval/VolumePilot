var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/health/live", () => TypedResults.Ok(new { status = "ok" }))
    .WithName("HealthLive");

app.MapGet("/api/health", () => TypedResults.Ok(new
{
    service = "VolumePilot HQ API",
    status = "ok",
}))
    .WithName("GetApiHealth");

app.Run();

public partial class Program
{
}
