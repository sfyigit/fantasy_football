using MatchDataIngestion;
using MatchDataIngestion.Services;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Shared.Data;
using Shared.Messaging;

var builder = Host.CreateApplicationBuilder(args);

// PostgreSQL via EF Core
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// RabbitMQ connection factory (singleton — channel is created lazily per publisher instance)
builder.Services.AddSingleton<IConnectionFactory>(_ => new ConnectionFactory
{
    HostName = builder.Configuration["RabbitMQ:Host"]     ?? "localhost",
    UserName = builder.Configuration["RabbitMQ:Username"] ?? "guest",
    Password = builder.Configuration["RabbitMQ:Password"] ?? "guest",
    Port     = int.TryParse(builder.Configuration["RabbitMQ:Port"], out var port) ? port : 5672
});

builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Apply pending EF Core migrations on startup
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

host.Run();
