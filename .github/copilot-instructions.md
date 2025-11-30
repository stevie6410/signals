# GitHub Copilot Instructions - SDHome Signals Project

## Project Overview

**SDHome Signals** is a .NET-based smart home automation backend that processes IoT sensor data from MQTT messages, stores events in SQL Server, and exposes a REST API for querying signal events, sensor readings, and trigger events.

## Architecture

### Solution Structure

```
signals/
├── src/
│   ├── SDHome.Api/          # ASP.NET Core Web API (.NET 10)
│   ├── SDHome.Lib/          # Shared library (models, services, data access)
│   └── ClientApp/           # Frontend client application
├── config/                  # Configuration files for infrastructure services
│   ├── grafana/             # Grafana datasource configuration
│   ├── mosquitto/           # MQTT broker configuration
│   ├── prometheus/          # Prometheus metrics configuration
│   └── zigbee/              # Zigbee2MQTT configuration
├── docker-compose.yml       # Full stack orchestration
└── dockerfile               # API container build
```

### Technology Stack

- **Runtime**: .NET 10 (Preview)
- **Web Framework**: ASP.NET Core Minimal API with Controllers
- **Database**: SQL Server (primary), PostgreSQL (supported)
- **Message Broker**: Eclipse Mosquitto (MQTT)
- **Logging**: Serilog with Seq sink
- **Metrics**: Prometheus + Grafana
- **API Documentation**: NSwag (OpenAPI/Swagger)
- **Containerization**: Docker & Docker Compose

### Core Components

#### SDHome.Api
- REST API exposing endpoints for signals, readings, and triggers
- Controllers: `SignalsController`, `ReadingsController`, `TriggersController`
- OpenAPI documentation via NSwag (auto-generates TypeScript client)
- CORS enabled for development

#### SDHome.Lib
- **Models**: `SignalEvent`, `SensorReading`, `TriggerEvent`, `DeviceKind`, `EventCategory`
- **Services**: 
  - `SignalsMqttWorker` - Background service subscribing to MQTT topics
  - `SignalsService` - Core business logic for processing MQTT messages
  - `SignalQueryService` - Query layer for signal events
  - `SignalEventProjectionService` - Projects signal events to triggers/readings
- **Data Access**: Repository pattern with SQL Server and PostgreSQL implementations
  - `ISignalEventsRepository`, `ISensorReadingsRepository`, `ITriggerEventsRepository`

## Key Patterns & Conventions

### Dependency Injection
- Use constructor injection with primary constructors
- Register services in `Program.cs`
- Use `IOptions<T>` pattern for configuration

### Configuration
- Configuration sections: `Signals:Mqtt`, `Signals:Postgres`, `Signals:MSSQL`, `Signals:Webhooks`
- Connection strings in `ConnectionStrings:DefaultConnection`
- Environment-specific settings via `ASPNETCORE_ENVIRONMENT`

### Data Models
- Use C# records for immutable data models
- `SignalEvent` is the core domain entity with:
  - Source, DeviceId, Location, Capability
  - EventType, EventSubType, Value
  - RawTopic, RawPayload (JsonElement)
  - DeviceKind, EventCategory enums

### API Conventions
- Route prefix: `/api/{resource}`
- Use `[FromQuery]` for optional parameters with defaults
- Return `List<T>` for collection endpoints
- Use async/await throughout

### Repository Pattern
- Interface-based repositories for testability
- `EnsureCreatedAsync()` for database initialization
- Support for both SQL Server and PostgreSQL

## Infrastructure Services (Docker Compose)

| Service | Port | Purpose |
|---------|------|---------|
| mosquitto | 1883 | MQTT broker for IoT messages |
| mosquitto-mc | 8088 | Mosquitto Management Center UI |
| seq | 5341 | Structured logging server |
| prometheus | 9090 | Metrics collection |
| grafana | 3000 | Metrics visualization |
| postgres | 5432 | PostgreSQL database |
| signals | 8090 | Main API application |
| zigbee2mqtt | 8080 | Zigbee device gateway |
| n8n | 5678 | Workflow automation |

## MQTT Topics

- Topic filter: `sdhome/#`
- Messages are processed by `SignalsMqttWorker` background service
- Payloads are mapped to `SignalEvent` via `ISignalEventMapper`

## Development Guidelines

### When Adding New Features
1. Define models in `SDHome.Lib/Models/`
2. Create repository interface in `SDHome.Lib/Data/`
3. Implement SQL Server repository
4. Add service layer in `SDHome.Lib/Services/`
5. Expose via controller in `SDHome.Api/Controllers/`

### When Working with MQTT
- Messages arrive via `SignalsMqttWorker.ExecuteAsync()`
- Processing happens in `ISignalsService.HandleMqttMessageAsync()`
- Use `ISignalEventMapper` for payload parsing

### Database Migrations
- Tables are created via `EnsureCreatedAsync()` at startup
- No EF Core - raw SQL with Dapper-style queries

### API Documentation
- NSwag generates OpenAPI spec on debug builds
- TypeScript client generated to `ClientApp/src/app/api/sdhome-client.ts`

## Code Style

- Use nullable reference types (`<Nullable>enable</Nullable>`)
- Use implicit usings (`<ImplicitUsings>enable</ImplicitUsings>`)
- Primary constructors for DI in controllers and services
- Collection expressions: `[.. enumerable]` syntax
- Async methods suffixed with `Async`

## Running the Project

### Local Development
```bash
# Start infrastructure
docker-compose up -d mosquitto seq prometheus grafana postgres

# Run API
cd src/SDHome.Api
dotnet run
```

### Full Stack (Docker)
```bash
docker-compose up -d
```

### API Endpoints
- Swagger UI: `http://localhost:8090/swagger`
- Signals: `GET /api/signals/logs?take=100`
- Readings: `GET /api/readings?take=100`
- Triggers: `GET /api/triggers?take=100`

## Environment Variables (Docker)

| Variable | Description |
|----------|-------------|
| `ASPNETCORE_ENVIRONMENT` | Environment name (Docker, Development) |
| `Signals__Mqtt__Host` | MQTT broker hostname |
| `Signals__Mqtt__Port` | MQTT broker port |
| `Signals__Postgres__ConnectionString` | PostgreSQL connection string |
| `Logging__SeqUrl` | Seq logging server URL |
