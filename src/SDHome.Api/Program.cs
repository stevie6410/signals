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

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Signals:Mqtt"));
builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection("Signals:Postgres"));
builder.Services.Configure<MsSQLOptions>(builder.Configuration.GetSection("Signals:MSSQL"));
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Signals:Webhooks"));
builder.Services.Configure<LoggingOptions>(builder.Configuration.GetSection("Logging"));
builder.Services.Configure<MetricsOptions>(builder.Configuration.GetSection("Metrics"));

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Repositories
builder.Services.AddSingleton<ISignalEventsRepository>(_ =>
    new SqlServerSignalEventsRepository(connectionString));

builder.Services.AddSingleton<ITriggerEventsRepository>(_ =>
    new SqlServerTriggerEventsRepository(connectionString));

builder.Services.AddSingleton<ISensorReadingsRepository>(_ =>
    new SqlServerSensorReadingsRepository(connectionString));

// Mapper & query service
builder.Services.AddSingleton<ISignalEventMapper, SignalEventMapper>();
builder.Services.AddScoped<ISignalQueryService, SignalQueryService>();

// Projection service (SignalEvent -> triggers + readings, etc.)
builder.Services.AddSingleton<ISignalEventProjectionService, SignalEventProjectionService>();

// HttpClient + main SignalsService + MQTT worker
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ISignalsService, SignalsService>();
builder.Services.AddHostedService<SignalsMqttWorker>();

var app = builder.Build();

app.UseCors("DevCors");

// Ensure SQL Server table/indexes exist at startup
// (EnsureCreatedAsync should now include creation of trigger_events and sensor_readings as well)
using (var scope = app.Services.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<ISignalEventsRepository>();
    await repo.EnsureCreatedAsync();
}

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
