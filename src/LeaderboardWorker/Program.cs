using LeaderboardWorker;
using LeaderboardWorker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using Shared.Data;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── PostgreSQL ────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// ── Redis ─────────────────────────────────────────────────────────────────────
var redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnectionString));

builder.Services.AddSingleton<LeaderboardService>();

// ── RabbitMQ ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IConnectionFactory>(_ => new ConnectionFactory
{
    HostName = builder.Configuration["RabbitMQ:Host"]     ?? "localhost",
    UserName = builder.Configuration["RabbitMQ:Username"] ?? "guest",
    Password = builder.Configuration["RabbitMQ:Password"] ?? "guest",
    Port     = int.TryParse(builder.Configuration["RabbitMQ:Port"], out var port) ? port : 5672
});

// ── Background service ────────────────────────────────────────────────────────
builder.Services.AddHostedService<Worker>();

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck("redis", () =>
    {
        try
        {
            var mux = builder.Services.BuildServiceProvider().GetRequiredService<IConnectionMultiplexer>();
            return mux.IsConnected
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Redis not connected");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    })
    .AddCheck("self", () => HealthCheckResult.Healthy());

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
