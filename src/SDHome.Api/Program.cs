using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SpaServices.Extensions;
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
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// NSwag OpenAPI generator
builder.Services.AddOpenApiDocument(options =>
{
    options.Title = "SDHome API";
    options.Version = "v1";
    options.DocumentName = "v1";  // <-- matches nswag.json documentName
});

builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Signals:Mqtt"));
builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection("Signals:Postgres"));
builder.Services.Configure<MsSQLOptions>(builder.Configuration.GetSection("Signals:MSSQL"));
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Signals:Webhooks"));
builder.Services.Configure<LoggingOptions>(builder.Configuration.GetSection("Logging"));
builder.Services.Configure<MetricsOptions>(builder.Configuration.GetSection("Metrics"));

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

// Repositories
builder.Services.AddSingleton<ISignalEventsRepository>(_ =>
    new SqlServerSignalEventsRepository(connectionString));
builder.Services.AddSingleton<SqlServerSignalEventsRepository>(_ =>
    new SqlServerSignalEventsRepository(connectionString));

builder.Services.AddSingleton<ITriggerEventsRepository>(_ =>
    new SqlServerTriggerEventsRepository(connectionString));

builder.Services.AddSingleton<ISensorReadingsRepository>(_ =>
    new SqlServerSensorReadingsRepository(connectionString));

builder.Services.AddSingleton<IDeviceRepository>(_ =>
    new SqlServerDeviceRepository(connectionString));

// Mapper & query service
builder.Services.AddSingleton<ISignalEventMapper, SignalEventMapper>();
builder.Services.AddScoped<ISignalQueryService, SignalQueryService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<DatabaseSeeder>();

// Projection service (SignalEvent -> triggers + readings, etc.)
builder.Services.AddSingleton<ISignalEventProjectionService, SignalEventProjectionService>();

// HttpClient + main SignalsService + MQTT worker
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ISignalsService, SignalsService>();

// MQTT Client for DeviceService - only register if MQTT is enabled
var mqttOptions = builder.Configuration.GetSection("Signals:Mqtt").Get<MqttOptions>();
if (mqttOptions?.Enabled == true)
{
    builder.Services.AddSingleton(sp =>
    {
        var factory = new MQTTnet.MqttClientFactory();
        return factory.CreateMqttClient();
    });
}

// Only add background workers if not in NSwag environment
if (!builder.Environment.IsEnvironment("NSwag"))
{
    builder.Services.AddHostedService<SignalsMqttWorker>();
}

var app = builder.Build();

app.UseCors("DevCors");

// Ensure SQL Server table/indexes exist at startup
// (EnsureCreatedAsync should now include creation of trigger_events and sensor_readings as well)
// Only ensure DB in normal runtime, not during NSwag or design-time builds
if (!app.Environment.IsEnvironment("NSwag"))
{
    using (var scope = app.Services.CreateScope())
    {
        var signalRepo = scope.ServiceProvider.GetRequiredService<ISignalEventsRepository>() as SqlServerSignalEventsRepository;
        if (signalRepo != null)
        {
            signalRepo.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        var deviceRepo = scope.ServiceProvider.GetRequiredService<IDeviceRepository>() as SqlServerDeviceRepository;
        if (deviceRepo != null)
        {
            deviceRepo.EnsureCreatedAsync().GetAwaiter().GetResult();
        }
    }
}

// Configure the HTTP request pipeline.
app.UseOpenApi();        // serves OpenAPI/Swagger document
app.UseSwaggerUi();      // serves Swagger UI at /swagger

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Serve Angular SPA
app.UseStaticFiles();

if (!app.Environment.IsEnvironment("NSwag"))
{
    app.UseSpa(spa =>
    {
        spa.Options.SourcePath = "../ClientApp";

        if (app.Environment.IsDevelopment())
        {
            // Proxy to Angular dev server during development
            spa.UseProxyToSpaDevelopmentServer("http://localhost:4200");
        }
    });
}

app.Run();
