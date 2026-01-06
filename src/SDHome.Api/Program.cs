using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SpaServices.Extensions;
using Microsoft.EntityFrameworkCore;
using SDHome.Api.HealthChecks;
using SDHome.Api.Hubs;
using SDHome.Api.Services;
using SDHome.Lib.Data;
using SDHome.Lib.Mappers;
using SDHome.Lib.Models;
using SDHome.Lib.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
    {
        policy
            .WithOrigins("http://localhost:4200", "http://localhost:5176")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// NSwag OpenAPI generator
builder.Services.AddOpenApiDocument(options =>
{
    options.Title = "SDHome API";
    options.Version = "v1";
    options.DocumentName = "v1";
});

builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Signals:Mqtt"));
builder.Services.Configure<MsSQLOptions>(builder.Configuration.GetSection("Signals:MSSQL"));
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Signals:Webhooks"));
builder.Services.Configure<DeviceStateSyncOptions>(builder.Configuration.GetSection("Signals:DeviceStateSync"));
builder.Services.Configure<LoggingOptions>(builder.Configuration.GetSection("Logging"));
builder.Services.Configure<MetricsOptions>(builder.Configuration.GetSection("Metrics"));

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// EF Core DbContext
builder.Services.AddDbContext<SignalsDbContext>(options =>
    options.UseSqlServer(connectionString));

// Services
builder.Services.AddSingleton<ISignalEventMapper, SignalEventMapper>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<ISignalEventProjectionService, SignalEventProjectionService>();
builder.Services.AddScoped<ISignalsService, SignalsService>();
builder.Services.AddScoped<IAutomationService, AutomationService>();
builder.Services.AddScoped<ICustomTriggerService, CustomTriggerService>();
builder.Services.AddScoped<DatabaseSeeder>();

// SignalR real-time event broadcaster
builder.Services.AddSingleton<IRealtimeEventBroadcaster, SignalREventBroadcaster>();

// End-to-end latency tracker
builder.Services.AddSingleton<IEndToEndLatencyTracker, EndToEndLatencyTracker>();

// HttpClient for webhook calls (used by SignalsService and AutomationEngine)
builder.Services.AddHttpClient<ISignalsService, SignalsService>();

// Automation Engine - register as singleton so it can be injected as IAutomationEngine
// Use a factory to properly inject HttpClient
builder.Services.AddHttpClient("AutomationEngine");
builder.Services.AddSingleton<AutomationEngine>(sp =>
{
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    var logger = sp.GetRequiredService<ILogger<AutomationEngine>>();
    var broadcaster = sp.GetRequiredService<IRealtimeEventBroadcaster>();
    var e2eTracker = sp.GetRequiredService<IEndToEndLatencyTracker>();
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("AutomationEngine");
    return new AutomationEngine(scopeFactory, logger, broadcaster, e2eTracker, httpClient);
});
builder.Services.AddSingleton<IAutomationEngine>(sp => sp.GetRequiredService<AutomationEngine>());

// Health Checks
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString!, name: "sqlserver", tags: ["db", "sql"])
    .AddCheck<MqttHealthCheck>("mqtt", tags: ["messaging"]);

// MQTT Client for DeviceService - only register if MQTT is enabled
var mqttOptions = builder.Configuration.GetSection("Signals:Mqtt").Get<MqttOptions>();
if (mqttOptions?.Enabled == true)
{
    builder.Services.AddSingleton(sp =>
    {
        var factory = new MQTTnet.MqttClientFactory();
        return factory.CreateMqttClient();
    });
    
    // Persistent MQTT publisher for fast command execution
    builder.Services.AddSingleton<MqttPublisher>();
    builder.Services.AddSingleton<IMqttPublisher>(sp => sp.GetRequiredService<MqttPublisher>());
}

// Only add background workers if not in NSwag environment
if (!builder.Environment.IsEnvironment("NSwag"))
{
    builder.Services.AddHostedService<SignalsMqttWorker>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AutomationEngine>());
    builder.Services.AddHostedService<DeviceStateSyncWorker>();
    
    // Start the MQTT publisher if enabled
    if (mqttOptions?.Enabled == true)
    {
        builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttPublisher>());
    }
}

var app = builder.Build();

app.UseCors("DevCors");

// Apply pending EF Core migrations at startup
// Skip during NSwag generation or design-time builds
if (!app.Environment.IsEnvironment("NSwag"))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SignalsDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        
        logger.LogInformation("Applying database migrations...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Failed to apply database migrations. API will start but database operations may fail.");
    }
}

// Configure the HTTP request pipeline.
app.UseOpenApi();        // serves OpenAPI/Swagger document
app.UseSwaggerUi();      // serves Swagger UI at /swagger

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapControllers();

// SignalR hub for real-time events
app.MapHub<SignalsHub>("/hubs/signals");

// Health check endpoints
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            })
        });
        await context.Response.WriteAsync(result);
    }
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("db")
});

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // Just checks if the app is running
});

// Serve static files (Angular dist)
app.UseStaticFiles();

// Serve Angular SPA - must be after all API/hub endpoints
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api") &&
               !context.Request.Path.StartsWithSegments("/health") &&
               !context.Request.Path.StartsWithSegments("/swagger") &&
               !context.Request.Path.StartsWithSegments("/hubs"),
    spaApp =>
    {
        spaApp.UseSpa(spa =>
        {
            spa.Options.SourcePath = "../ClientApp";

            // Only proxy to Angular dev server if explicitly requested via environment variable
            // Set PROXY_TO_SPA=true to enable (used by "Full Stack" launch config)
            var proxySpa = Environment.GetEnvironmentVariable("PROXY_TO_SPA") == "true";
            if (proxySpa)
            {
                spa.UseProxyToSpaDevelopmentServer("http://localhost:4200");
            }
        });
    });

app.Run();
