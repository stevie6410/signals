using System.Data;
using Microsoft.Data.SqlClient;
using SDHome.Lib.Models;

namespace SDHome.Lib.Data
{
    public class SqlServerTriggerEventsRepository(string connectionString) : ITriggerEventsRepository
    {
        private readonly string _connectionString = connectionString;

        public async Task InsertAsync(TriggerEvent ev, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                                INSERT INTO dbo.trigger_events
                                    (id, signal_event_id, timestamp_utc, device_id, capability,
                                    trigger_type, trigger_sub_type, value_bit)
                                VALUES
                                    (@Id, @SignalEventId, @TimestampUtc, @DeviceId, @Capability,
                                    @TriggerType, @TriggerSubType, @ValueBit);";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = ev.Id;
            cmd.Parameters.Add("@SignalEventId", SqlDbType.UniqueIdentifier).Value = ev.SignalEventId;
            cmd.Parameters.Add("@TimestampUtc", SqlDbType.DateTime2).Value = ev.TimestampUtc;
            cmd.Parameters.Add("@DeviceId", SqlDbType.NVarChar, 200).Value = ev.DeviceId;
            cmd.Parameters.Add("@Capability", SqlDbType.NVarChar, 200).Value = ev.Capability;
            cmd.Parameters.Add("@TriggerType", SqlDbType.NVarChar, 100).Value = ev.TriggerType;
            cmd.Parameters.Add("@TriggerSubType", SqlDbType.NVarChar, 100).Value = (object?)ev.TriggerSubType ?? DBNull.Value;
            cmd.Parameters.Add("@ValueBit", SqlDbType.Bit).Value = (object?)ev.Value ?? DBNull.Value;

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<TriggerEvent>> GetRecentAsync(
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                                SELECT TOP (@Take)
                                    id, signal_event_id, timestamp_utc, device_id, capability,
                                    trigger_type, trigger_sub_type, value_bit
                                FROM dbo.trigger_events
                                ORDER BY timestamp_utc DESC;";

            var list = new List<TriggerEvent>();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@Take", SqlDbType.Int).Value = take;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var ordId = reader.GetOrdinal("id");
            var ordSignalId = reader.GetOrdinal("signal_event_id");
            var ordTs = reader.GetOrdinal("timestamp_utc");
            var ordDeviceId = reader.GetOrdinal("device_id");
            var ordCapability = reader.GetOrdinal("capability");
            var ordTriggerType = reader.GetOrdinal("trigger_type");
            var ordTriggerSubType = reader.GetOrdinal("trigger_sub_type");
            var ordValueBit = reader.GetOrdinal("value_bit");

            while (await reader.ReadAsync(cancellationToken))
            {
                bool? value = reader.IsDBNull(ordValueBit) ? null : reader.GetBoolean(ordValueBit);
                string? triggerSubType = reader.IsDBNull(ordTriggerSubType)
                    ? null
                    : reader.GetString(ordTriggerSubType);

                list.Add(new TriggerEvent(
                    Id: reader.GetGuid(ordId),
                    SignalEventId: reader.GetGuid(ordSignalId),
                    TimestampUtc: reader.GetDateTime(ordTs),
                    DeviceId: reader.GetString(ordDeviceId),
                    Capability: reader.GetString(ordCapability),
                    TriggerType: reader.GetString(ordTriggerType),
                    TriggerSubType: triggerSubType,
                    Value: value
                ));
            }

            return list;
        }

        public async Task<IReadOnlyList<TriggerEvent>> GetByDeviceAsync(
            string deviceId,
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                                SELECT TOP (@Take)
                                    id, signal_event_id, timestamp_utc, device_id, capability,
                                    trigger_type, trigger_sub_type, value_bit
                                FROM dbo.trigger_events
                                WHERE device_id = @DeviceId
                                ORDER BY timestamp_utc DESC;";

            var list = new List<TriggerEvent>();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@DeviceId", SqlDbType.NVarChar, 200).Value = deviceId;
            cmd.Parameters.Add("@Take", SqlDbType.Int).Value = take;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var ordId = reader.GetOrdinal("id");
            var ordSignalId = reader.GetOrdinal("signal_event_id");
            var ordTs = reader.GetOrdinal("timestamp_utc");
            var ordDeviceId = reader.GetOrdinal("device_id");
            var ordCapability = reader.GetOrdinal("capability");
            var ordTriggerType = reader.GetOrdinal("trigger_type");
            var ordTriggerSubType = reader.GetOrdinal("trigger_sub_type");
            var ordValueBit = reader.GetOrdinal("value_bit");

            while (await reader.ReadAsync(cancellationToken))
            {
                bool? value = reader.IsDBNull(ordValueBit) ? null : reader.GetBoolean(ordValueBit);
                string? triggerSubType = reader.IsDBNull(ordTriggerSubType)
                    ? null
                    : reader.GetString(ordTriggerSubType);

                list.Add(new TriggerEvent(
                    Id: reader.GetGuid(ordId),
                    SignalEventId: reader.GetGuid(ordSignalId),
                    TimestampUtc: reader.GetDateTime(ordTs),
                    DeviceId: reader.GetString(ordDeviceId),
                    Capability: reader.GetString(ordCapability),
                    TriggerType: reader.GetString(ordTriggerType),
                    TriggerSubType: triggerSubType,
                    Value: value
                ));
            }

            return list;
        }
    }
}
