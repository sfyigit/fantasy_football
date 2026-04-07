using System.Threading.RateLimiting;
using MatchApi;
using MatchApi.Auth;
using MatchApi.Dispatcher;
using MatchApi.Handlers;
using MatchApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Shared.Contracts;
using Shared.Data;
using Shared.Domain.Entities;
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

// ── JWT ───────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<JwtService>();

// ── WebSocket services ────────────────────────────────────────────────────────
builder.Services.AddSingleton<SubscriptionManager>();
builder.Services.AddHostedService<LiveScorePushService>();

// ── Opcode handlers ───────────────────────────────────────────────────────────
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
                PermitLimit       = 60,
                Window            = TimeSpan.FromMinutes(1),
                QueueLimit        = 0,
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
    c.AddSecurityDefinition("Bearer", new()
    {
        Name         = "Authorization",
        Type         = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description  = "Enter your JWT token"
    });
    c.AddSecurityRequirement(new()
    {
        {
            new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
            []
        }
    });
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

// ── Auth endpoints (REST — outside opcode system) ─────────────────────────────

// POST /auth/register
app.MapPost("/auth/register",
    async ([FromBody] RegisterRequest req, AppDbContext db, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(req.Username) ||
            string.IsNullOrWhiteSpace(req.Email)    ||
            string.IsNullOrWhiteSpace(req.Password))
            return Results.BadRequest(new { error = "username, email, and password are required" });

        bool emailTaken = await db.Users.AnyAsync(u => u.Email == req.Email, ct);
        if (emailTaken)
            return Results.Conflict(new { error = "Email is already registered" });

        var user = new User
        {
            Id           = Guid.NewGuid().ToString(),
            Username     = req.Username.Trim(),
            Email        = req.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            CreatedAt    = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { userId = user.Id, username = user.Username, email = user.Email });
    })
    .RequireRateLimiting("opcode-ip")
    .WithName("Register")
    .WithOpenApi()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status409Conflict);

// POST /auth/login
app.MapPost("/auth/login",
    async ([FromBody] LoginRequest req, AppDbContext db, JwtService jwt, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return Results.BadRequest(new { error = "email and password are required" });

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Email == req.Email.Trim().ToLowerInvariant(), ct);

        if (user is null || user.PasswordHash is null ||
            !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Results.Unauthorized();

        var (token, expiresIn) = jwt.GenerateToken(user.Id, user.Username, user.Email);

        return Results.Ok(new
        {
            accessToken = token,
            expiresIn,
            userId   = user.Id,
            username = user.Username
        });
    })
    .RequireRateLimiting("opcode-ip")
    .WithName("Login")
    .WithOpenApi()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized);

// ── WebSocket endpoint ────────────────────────────────────────────────────────
app.Map("/ws", async (
    HttpContext context,
    OpcodeDispatcher dispatcher,
    SubscriptionManager subscriptions,
    JwtService jwt,
    ILoggerFactory loggerFactory) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket upgrade required");
        return;
    }

    // Extract JWT once from the upgrade handshake — auth context is fixed for the session lifetime
    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
    var auth       = jwt.ValidateAuthorizationHeader(authHeader);

    var ws      = await context.WebSockets.AcceptWebSocketAsync();
    var session = new WebSocketSession(
        ws, dispatcher, subscriptions, auth,
        loggerFactory.CreateLogger<WebSocketSession>());

    await session.RunAsync(context.RequestAborted);
}).RequireRateLimiting("opcode-ip");

// ── HTTP opcode fallback ──────────────────────────────────────────────────────
app.MapPost("/opcode",
    async (HttpContext context, [FromBody] OpcodeRequest request,
           OpcodeDispatcher dispatcher, JwtService jwt, CancellationToken ct) =>
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        var auth       = jwt.ValidateAuthorizationHeader(authHeader);

        var response = await dispatcher.DispatchAsync(request, null, auth, ct);
        return Results.Json(response, ApiJsonOptions.Options);
    })
    .RequireRateLimiting("opcode-ip")
    .WithName("PostOpcode")
    .WithOpenApi()
    .Produces<OpcodeResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status429TooManyRequests);

// ── Health endpoints ──────────────────────────────────────────────────────────
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

// ── Auth request DTOs (local to Program.cs) ───────────────────────────────────
record RegisterRequest(string Username, string Email, string Password);
record LoginRequest(string Email, string Password);
