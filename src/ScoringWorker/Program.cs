using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Shared.Data;
using Shared.Messaging;
using ScoringWorker;
using ScoringWorker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

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
host.Run();
