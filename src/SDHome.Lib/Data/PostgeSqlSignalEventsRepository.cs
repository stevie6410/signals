using Npgsql;
using NpgsqlTypes;
using SDHome.Lib.Models;
using System.Text.Json;

namespace SDHome.Lib.Data
{
    public class PostgresSignalEventsRepository(string connectionString) : ISignalEventsRepository
    {
        private readonly string _connectionString = connectionString;

        public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
        {
            const string sql = @"
                    CREATE TABLE IF NOT EXISTS signal_events (
                        id              uuid PRIMARY KEY,
                        timestamp_utc   timestamptz      NOT NULL,
                        source          text             NOT NULL,
                        device_id       text             NOT NULL,
                        location        text             NULL,
                        capability      text             NOT NULL,
                        event_type      text             NOT NULL,
                        event_sub_type  text             NULL,
                        value           double precision NULL,
                        raw_topic       text             NOT NULL,
                        raw_payload     jsonb            NOT NULL
                    );

                    CREATE INDEX IF NOT EXISTS ix_signal_events_timestamp
                        ON signal_events (timestamp_utc);

                    CREATE INDEX IF NOT EXISTS ix_signal_events_device_timestamp
                        ON signal_events (device_id, timestamp_utc DESC);
                ";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task InsertAsync(SignalEvent ev, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                                INSERT INTO signal_events
                                    (id, timestamp_utc, source, device_id, location, capability,
                                     event_type, event_sub_type, value, raw_topic, raw_payload)
                                VALUES
                                    (@Id, @TimestampUtc, @Source, @DeviceId, @Location, @Capability,
                                     @EventType, @EventSubType, @Value, @RawTopic, @RawPayload);";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@Id", ev.Id);
            cmd.Parameters.AddWithValue("@TimestampUtc", ev.TimestampUtc);
            cmd.Parameters.AddWithValue("@Source", ev.Source);
            cmd.Parameters.AddWithValue("@DeviceId", ev.DeviceId);
            cmd.Parameters.AddWithValue("@Location", (object?)ev.Location ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Capability", ev.Capability);
            cmd.Parameters.AddWithValue("@EventType", ev.EventType);
            cmd.Parameters.AddWithValue("@EventSubType", (object?)ev.EventSubType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Value", (object?)ev.Value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RawTopic", ev.RawTopic);

            var rawPayloadJson = JsonSerializer.Serialize(ev.RawPayload);
            cmd.Parameters.AddWithValue("@RawPayload", NpgsqlDbType.Jsonb, rawPayloadJson);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<SignalEvent>> GetRecentAsync(int take = 100, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                                SELECT id, timestamp_utc, source, device_id, location, capability, event_type, event_sub_type, value, raw_topic, raw_payload
                                FROM signal_events
                                ORDER BY timestamp_utc DESC
                                LIMIT @Take;";

            var list = new List<SignalEvent>();

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Take", take);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var json = (string)reader["raw_payload"];
                using var doc = JsonDocument.Parse(json);
                var payload = doc.RootElement.Clone();

                list.Add(new SignalEvent(
                    Id: reader.GetGuid(reader.GetOrdinal("id")),
                    TimestampUtc: reader.GetDateTime(reader.GetOrdinal("timestamp_utc")),
                    Source: reader.GetString(reader.GetOrdinal("source")),
                    DeviceId: reader.GetString(reader.GetOrdinal("device_id")),
                    Location: reader["location"] as string,
                    Capability: reader.GetString(reader.GetOrdinal("capability")),
                    EventType: reader.GetString(reader.GetOrdinal("event_type")),
                    EventSubType: reader["event_sub_type"] as string,
                    Value: reader["value"] as double?,
                    RawTopic: reader.GetString(reader.GetOrdinal("raw_topic")),
                    RawPayload: payload,
                    DeviceKind: DeviceKind.Unknown,        // if you want, store this in DB too later
                    EventCategory: EventCategory.Telemetry,
                    RawPayloadArray: null
                ));
            }

            return list;
        }

        public async Task<IReadOnlyList<SignalEvent>> GetByDeviceAsync(string deviceId, int take = 100, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                                SELECT id, timestamp_utc, source, device_id, location, capability,
                                       event_type, event_sub_type, value, raw_topic, raw_payload
                                FROM signal_events
                                WHERE device_id = @DeviceId
                                ORDER BY timestamp_utc DESC
                                LIMIT @Take;";

            var list = new List<SignalEvent>();

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@DeviceId", deviceId);
            cmd.Parameters.AddWithValue("@Take", take);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var json = (string)reader["raw_payload"];
                using var doc = JsonDocument.Parse(json);
                var payload = doc.RootElement.Clone();

                list.Add(new SignalEvent(
                    Id: reader.GetGuid(reader.GetOrdinal("id")),
                    TimestampUtc: reader.GetDateTime(reader.GetOrdinal("timestamp_utc")),
                    Source: reader.GetString(reader.GetOrdinal("source")),
                    DeviceId: reader.GetString(reader.GetOrdinal("device_id")),
                    Location: reader["location"] as string,
                    Capability: reader.GetString(reader.GetOrdinal("capability")),
                    EventType: reader.GetString(reader.GetOrdinal("event_type")),
                    EventSubType: reader["event_sub_type"] as string,
                    Value: reader["value"] as double?,
                    RawTopic: reader.GetString(reader.GetOrdinal("raw_topic")),
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
