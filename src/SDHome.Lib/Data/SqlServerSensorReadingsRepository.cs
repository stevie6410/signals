using System.Data;
using Microsoft.Data.SqlClient;
using SDHome.Lib.Models;

namespace SDHome.Lib.Data
{
    public class SqlServerSensorReadingsRepository(string connectionString) : ISensorReadingsRepository
    {
        private readonly string _connectionString = connectionString;

        public async Task InsertManyAsync(IEnumerable<SensorReading> readings, CancellationToken cancellationToken = default)
        {
            const string sql = @"
INSERT INTO dbo.sensor_readings
    (id, signal_event_id, timestamp_utc, device_id, metric, value, unit)
VALUES
    (@Id, @SignalEventId, @TimestampUtc, @DeviceId, @Metric, @Value, @Unit);";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            foreach (var r in readings)
            {
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = r.Id;
                cmd.Parameters.Add("@SignalEventId", SqlDbType.UniqueIdentifier).Value = r.SignalEventId;
                cmd.Parameters.Add("@TimestampUtc", SqlDbType.DateTime2).Value = r.TimestampUtc;
                cmd.Parameters.Add("@DeviceId", SqlDbType.NVarChar, 200).Value = r.DeviceId;
                cmd.Parameters.Add("@Metric", SqlDbType.NVarChar, 100).Value = r.Metric;
                cmd.Parameters.Add("@Value", SqlDbType.Float).Value = r.Value;
                cmd.Parameters.Add("@Unit", SqlDbType.NVarChar, 50).Value = (object?)r.Unit ?? DBNull.Value;

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        public async Task<IReadOnlyList<SensorReading>> GetRecentAsync(
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
SELECT TOP (@Take)
       id, signal_event_id, timestamp_utc, device_id, metric, value, unit
FROM dbo.sensor_readings
ORDER BY timestamp_utc DESC;";

            var list = new List<SensorReading>();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@Take", SqlDbType.Int).Value = take;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var ordId = reader.GetOrdinal("id");
            var ordSignalId = reader.GetOrdinal("signal_event_id");
            var ordTs = reader.GetOrdinal("timestamp_utc");
            var ordDeviceId = reader.GetOrdinal("device_id");
            var ordMetric = reader.GetOrdinal("metric");
            var ordValue = reader.GetOrdinal("value");
            var ordUnit = reader.GetOrdinal("unit");

            while (await reader.ReadAsync(cancellationToken))
            {
                string? unit = reader.IsDBNull(ordUnit) ? null : reader.GetString(ordUnit);

                list.Add(new SensorReading(
                    Id: reader.GetGuid(ordId),
                    SignalEventId: reader.GetGuid(ordSignalId),
                    TimestampUtc: reader.GetDateTime(ordTs),
                    DeviceId: reader.GetString(ordDeviceId),
                    Metric: reader.GetString(ordMetric),
                    Value: reader.GetDouble(ordValue),
                    Unit: unit
                ));
            }

            return list;
        }

        public async Task<IReadOnlyList<SensorReading>> GetByDeviceAndMetricAsync(
            string deviceId,
            string metric,
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
SELECT TOP (@Take)
       id, signal_event_id, timestamp_utc, device_id, metric, value, unit
FROM dbo.sensor_readings
WHERE device_id = @DeviceId AND metric = @Metric
ORDER BY timestamp_utc DESC;";

            var list = new List<SensorReading>();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@DeviceId", SqlDbType.NVarChar, 200).Value = deviceId;
            cmd.Parameters.Add("@Metric", SqlDbType.NVarChar, 100).Value = metric;
            cmd.Parameters.Add("@Take", SqlDbType.Int).Value = take;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var ordId = reader.GetOrdinal("id");
            var ordSignalId = reader.GetOrdinal("signal_event_id");
            var ordTs = reader.GetOrdinal("timestamp_utc");
            var ordDeviceId = reader.GetOrdinal("device_id");
            var ordMetric = reader.GetOrdinal("metric");
            var ordValue = reader.GetOrdinal("value");
            var ordUnit = reader.GetOrdinal("unit");

            while (await reader.ReadAsync(cancellationToken))
            {
                string? unit = reader.IsDBNull(ordUnit) ? null : reader.GetString(ordUnit);

                list.Add(new SensorReading(
                    Id: reader.GetGuid(ordId),
                    SignalEventId: reader.GetGuid(ordSignalId),
                    TimestampUtc: reader.GetDateTime(ordTs),
                    DeviceId: reader.GetString(ordDeviceId),
                    Metric: reader.GetString(ordMetric),
                    Value: reader.GetDouble(ordValue),
                    Unit: unit
                ));
            }

            return list;
        }
    }
}
