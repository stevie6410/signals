using global::SDHome.Lib.Models;
using System.Text;
using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace SDHome.Lib.Data
{
    public class SqlServerSignalEventsRepository(string connectionString) : ISignalEventsRepository
    {
        private readonly string _connectionString = connectionString;

        public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
        {
            // SQL Server DDL: use IF OBJECT_ID IS NULL instead of CREATE TABLE IF NOT EXISTS
            const string sql = @"
                           IF OBJECT_ID('dbo.trigger_events', 'U') IS NULL
                            BEGIN
                            CREATE TABLE dbo.trigger_events (
                            id uniqueidentifier NOT NULL PRIMARY KEY,
                            signal_event_id uniqueidentifier NOT NULL,
                            timestamp_utc datetime2(7) NOT NULL,
                            device_id nvarchar(200) NOT NULL,
                            capability nvarchar(200) NOT NULL,
                            trigger_type nvarchar(100) NOT NULL,
                            trigger_sub_type nvarchar(100) NULL,
                            value_bit bit NULL
                            );

                            CREATE INDEX ix_trigger_events_device_timestamp
                                ON dbo.trigger_events (device_id, timestamp_utc DESC);

                            CREATE INDEX ix_trigger_events_type_timestamp
                                ON dbo.trigger_events (trigger_type, timestamp_utc DESC);


                            END;

                            IF OBJECT_ID('dbo.sensor_readings', 'U') IS NULL
                            BEGIN
                            CREATE TABLE dbo.sensor_readings (
                            id uniqueidentifier NOT NULL PRIMARY KEY,
                            signal_event_id uniqueidentifier NOT NULL,
                            timestamp_utc datetime2(7) NOT NULL,
                            device_id nvarchar(200) NOT NULL,
                            metric nvarchar(100) NOT NULL,
                            value float NOT NULL,
                            unit nvarchar(50) NULL
                            );

                            CREATE INDEX ix_sensor_readings_device_metric_ts
                                ON dbo.sensor_readings (device_id, metric, timestamp_utc DESC);

                            CREATE INDEX ix_sensor_readings_metric_ts
                                ON dbo.sensor_readings (metric, timestamp_utc DESC);


                            END;
                            ";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task InsertAsync(SignalEvent ev, CancellationToken cancellationToken = default)
        {
            const string sql = @"
        INSERT INTO dbo.signal_events
            (id, timestamp_utc, source, device_id, location, capability,
             event_type, event_sub_type, value, raw_topic, raw_payload)
        VALUES
            (@Id, @TimestampUtc, @Source, @DeviceId, @Location, @Capability,
             @EventType, @EventSubType, @Value, @RawTopic, @RawPayload);";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand(sql, conn);

            cmd.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = ev.Id;
            cmd.Parameters.Add("@TimestampUtc", SqlDbType.DateTime2).Value = ev.TimestampUtc;
            cmd.Parameters.Add("@Source", SqlDbType.NVarChar, 200).Value = ev.Source;
            cmd.Parameters.Add("@DeviceId", SqlDbType.NVarChar, 200).Value = ev.DeviceId;
            cmd.Parameters.Add("@Location", SqlDbType.NVarChar, 200).Value = (object?)ev.Location ?? DBNull.Value;
            cmd.Parameters.Add("@Capability", SqlDbType.NVarChar, 200).Value = ev.Capability;
            cmd.Parameters.Add("@EventType", SqlDbType.NVarChar, 200).Value = ev.EventType;
            cmd.Parameters.Add("@EventSubType", SqlDbType.NVarChar, 200).Value = (object?)ev.EventSubType ?? DBNull.Value;

            if (ev.Value.HasValue)
                cmd.Parameters.Add("@Value", SqlDbType.Float).Value = ev.Value.Value;
            else
                cmd.Parameters.Add("@Value", SqlDbType.Float).Value = DBNull.Value;

            cmd.Parameters.Add("@RawTopic", SqlDbType.NVarChar, 4000).Value = ev.RawTopic;

            var rawPayloadJson = JsonSerializer.Serialize(ev.RawPayload);
            cmd.Parameters.Add("@RawPayload", SqlDbType.NVarChar, -1).Value = rawPayloadJson; // -1 = NVARCHAR(MAX)

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<SignalEvent>> GetRecentAsync(int take = 100, CancellationToken cancellationToken = default)
        {
            const string sql = @"
SELECT TOP (@Take)
       id, timestamp_utc, source, device_id, location, capability,
       event_type, event_sub_type, value, raw_topic, raw_payload
FROM dbo.signal_events
WHERE device_id NOT LIKE 'bridge/%'
ORDER BY timestamp_utc DESC;";

            var list = new List<SignalEvent>();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@Take", SqlDbType.Int).Value = take;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var ordId = reader.GetOrdinal("id");
            var ordTs = reader.GetOrdinal("timestamp_utc");
            var ordSource = reader.GetOrdinal("source");
            var ordDeviceId = reader.GetOrdinal("device_id");
            var ordLocation = reader.GetOrdinal("location");
            var ordCapability = reader.GetOrdinal("capability");
            var ordEventType = reader.GetOrdinal("event_type");
            var ordEventSubType = reader.GetOrdinal("event_sub_type");
            var ordValue = reader.GetOrdinal("value");
            var ordRawTopic = reader.GetOrdinal("raw_topic");
            var ordRawPayload = reader.GetOrdinal("raw_payload");

            while (await reader.ReadAsync(cancellationToken))
            {
                var json = reader.GetString(ordRawPayload);
                using var doc = JsonDocument.Parse(json);
                var payload = doc.RootElement.Clone();

                string? location = reader.IsDBNull(ordLocation) ? null : reader.GetString(ordLocation);
                string? eventSubType = reader.IsDBNull(ordEventSubType) ? null : reader.GetString(ordEventSubType);
                double? value = reader.IsDBNull(ordValue) ? null : reader.GetDouble(ordValue);

                list.Add(new SignalEvent(
                    Id: reader.GetGuid(ordId),
                    TimestampUtc: reader.GetDateTime(ordTs),
                    Source: reader.GetString(ordSource),
                    DeviceId: reader.GetString(ordDeviceId),
                    Location: location,
                    Capability: reader.GetString(ordCapability),
                    EventType: reader.GetString(ordEventType),
                    EventSubType: eventSubType,
                    Value: value,
                    RawTopic: reader.GetString(ordRawTopic),
                    RawPayload: payload,
                    DeviceKind: DeviceKind.Unknown,
                    EventCategory: EventCategory.Telemetry,
                    RawPayloadArray: null
                ));
            }

            return list;
        }

        public async Task<IReadOnlyList<SignalEvent>> GetByDeviceAsync(
            string deviceId,
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
SELECT TOP (@Take)
       id, timestamp_utc, source, device_id, location, capability,
       event_type, event_sub_type, value, raw_topic, raw_payload
FROM dbo.signal_events
WHERE device_id = @DeviceId
ORDER BY timestamp_utc DESC;";

            var list = new List<SignalEvent>();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@DeviceId", SqlDbType.NVarChar, 200).Value = deviceId;
            cmd.Parameters.Add("@Take", SqlDbType.Int).Value = take;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var ordId = reader.GetOrdinal("id");
            var ordTs = reader.GetOrdinal("timestamp_utc");
            var ordSource = reader.GetOrdinal("source");
            var ordDeviceId = reader.GetOrdinal("device_id");
            var ordLocation = reader.GetOrdinal("location");
            var ordCapability = reader.GetOrdinal("capability");
            var ordEventType = reader.GetOrdinal("event_type");
            var ordEventSubType = reader.GetOrdinal("event_sub_type");
            var ordValue = reader.GetOrdinal("value");
            var ordRawTopic = reader.GetOrdinal("raw_topic");
            var ordRawPayload = reader.GetOrdinal("raw_payload");

            while (await reader.ReadAsync(cancellationToken))
            {
                var json = reader.GetString(ordRawPayload);
                using var doc = JsonDocument.Parse(json);
                var payload = doc.RootElement.Clone();

                string? location = reader.IsDBNull(ordLocation) ? null : reader.GetString(ordLocation);
                string? eventSubType = reader.IsDBNull(ordEventSubType) ? null : reader.GetString(ordEventSubType);
                double? value = reader.IsDBNull(ordValue) ? null : reader.GetDouble(ordValue);

                list.Add(new SignalEvent(
                    Id: reader.GetGuid(ordId),
                    TimestampUtc: reader.GetDateTime(ordTs),
                    Source: reader.GetString(ordSource),
                    DeviceId: reader.GetString(ordDeviceId),
                    Location: location,
                    Capability: reader.GetString(ordCapability),
                    EventType: reader.GetString(ordEventType),
                    EventSubType: eventSubType,
                    Value: value,
                    RawTopic: reader.GetString(ordRawTopic),
                    RawPayload: payload,
                    DeviceKind: DeviceKind.Unknown,
                    EventCategory: EventCategory.Telemetry,
                    RawPayloadArray: null
                ));
            }

            return list;
        }
    }
}

