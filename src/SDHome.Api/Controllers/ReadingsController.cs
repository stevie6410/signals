using Microsoft.AspNetCore.Mvc;
using SDHome.Lib.Data;
using SDHome.Lib.Models;

namespace SDHome.Api.Controllers
{
    [ApiController]
    [Route("/api/readings")]
    public class ReadingsController(ISensorReadingsRepository repository) : ControllerBase
    {
        [HttpGet]
        public async Task<List<SensorReading>> GetRecentReadings([FromQuery] int take = 100)
        {
            var res = await repository.GetRecentAsync(take);
            return [.. res];
        }

        [HttpGet("{deviceId}/{metric}")]
        public async Task<List<SensorReading>> GetReadingsForDeviceAndMetric(
            string deviceId,
            string metric,
            [FromQuery] int take = 100)
        {
            var res = await repository.GetByDeviceAndMetricAsync(deviceId, metric, take);
            return [.. res];
        }
    }
}

