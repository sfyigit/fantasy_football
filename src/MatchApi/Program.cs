using System.Threading.RateLimiting;
using MatchApi;
using MatchApi.Dispatcher;
using MatchApi.Handlers;
using MatchApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Shared.Contracts;
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

// ── RabbitMQ ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IConnectionFactory>(_ => new ConnectionFactory
{
    HostName = builder.Configuration["RabbitMQ:Host"]     ?? "localhost",
    UserName = builder.Configuration["RabbitMQ:Username"] ?? "guest",
    Password = builder.Configuration["RabbitMQ:Password"] ?? "guest",
    Port     = int.TryParse(builder.Configuration["RabbitMQ:Port"], out var port) ? port : 5672
});

// ── WebSocket services ────────────────────────────────────────────────────────
builder.Services.AddSingleton<SubscriptionManager>();
builder.Services.AddHostedService<LiveScorePushService>();

// ── Opcode handlers (all registered as IOpcodeHandler) ───────────────────────
builder.Services.AddSingleton<IOpcodeHandler, HeartbeatHandler>();
builder.Services.AddSingleton<IOpcodeHandler, GetMatchesHandler>();
builder.Services.AddSingleton<IOpcodeHandler, SubscribeLiveScoreHandler>();
builder.Services.AddSingleton<IOpcodeHandler, UnsubscribeLiveScoreHandler>();
builder.Services.AddSingleton<IOpcodeHandler, SaveSquadHandler>();
builder.Services.AddSingleton<IOpcodeHandler, GetMyScoreHandler>();
builder.Services.AddSingleton<IOpcodeHandler, GetPlayerStatsHandler>();
builder.Services.AddSingleton<IOpcodeHandler, GetLeaderboardHandler>();
builder.Services.AddSingleton<IOpcodeHandler, GetMyRankHandler>();

builder.Services.AddSingleton<OpcodeDispatcher>();

// ── Rate limiting (IP-based fixed window) ────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("opcode-ip", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit    = 60,
                Window         = TimeSpan.FromMinutes(1),
                QueueLimit     = 0,
                AutoReplenishment = true
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "MatchApi", Version = "v1" });
});

var app = builder.Build();

// Apply EF Core migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRateLimiter();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

// ── Endpoints ─────────────────────────────────────────────────────────────────

// WebSocket endpoint — all 11 opcodes over a persistent connection
app.Map("/ws", async (
    HttpContext context,
    OpcodeDispatcher dispatcher,
    SubscriptionManager subscriptions,
    ILoggerFactory loggerFactory) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket upgrade required");
        return;
    }

    var ws      = await context.WebSockets.AcceptWebSocketAsync();
    var session = new WebSocketSession(
        ws, dispatcher, subscriptions,
        loggerFactory.CreateLogger<WebSocketSession>());

    await session.RunAsync(context.RequestAborted);
}).RequireRateLimiting("opcode-ip");

// HTTP fallback — for non-streaming opcodes (1001, 2001, 2002, 2003, 3001, 3002)
app.MapPost("/opcode",
    async ([FromBody] OpcodeRequest request, OpcodeDispatcher dispatcher, CancellationToken ct) =>
    {
        var response = await dispatcher.DispatchAsync(request, null, ct);
        return Results.Json(response, ApiJsonOptions.Options);
    })
    .RequireRateLimiting("opcode-ip")
    .WithName("PostOpcode")
    .WithOpenApi()
    .Produces<OpcodeResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status429TooManyRequests);

// Health endpoints
app.MapHealthChecks("/health");

app.MapGet("/ready", async (AppDbContext db, IConnectionMultiplexer redis) =>
{
    try
    {
        await db.Database.CanConnectAsync();
        await redis.GetDatabase().PingAsync();
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).WithName("Ready").WithOpenApi();

app.Run();
