using MatchDataIngestion;
using MatchDataIngestion.Services;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Shared.Data;
using Shared.Messaging;

var builder = WebApplication.CreateBuilder(args);

// ── PostgreSQL via EF Core ────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// ── RabbitMQ ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IConnectionFactory>(_ => new ConnectionFactory
{
    HostName = builder.Configuration["RabbitMQ:Host"]     ?? "localhost",
    UserName = builder.Configuration["RabbitMQ:Username"] ?? "guest",
    Password = builder.Configuration["RabbitMQ:Password"] ?? "guest",
    Port     = int.TryParse(builder.Configuration["RabbitMQ:Port"], out var port) ? port : 5672
});

builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<OutboxRelayService>();

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

var app = builder.Build();

// Apply pending EF Core migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.MapHealthChecks("/health");

app.MapGet("/ready", async (AppDbContext db) =>
{
    try
    {
        await db.Database.CanConnectAsync();
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();
