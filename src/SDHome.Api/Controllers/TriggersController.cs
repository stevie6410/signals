using Microsoft.AspNetCore.Mvc;
using SDHome.Lib.Data;
using SDHome.Lib.Models;

namespace SDHome.Api.Controllers
{
    [ApiController]
    [Route("/api/triggers")]
    public class TriggersController(ITriggerEventsRepository repository) : ControllerBase
    {
        [HttpGet]
        public async Task<List<TriggerEvent>> GetRecentTriggers([FromQuery] int take = 100)
        {
            var res = await repository.GetRecentAsync(take);
            return [.. res];
        }

        [HttpGet("{deviceId}")]
        public async Task<List<TriggerEvent>> GetTriggersForDevice(
            string deviceId,
            [FromQuery] int take = 100)
        {
            var res = await repository.GetByDeviceAsync(deviceId, take);
            return [.. res];
        }
    }
}

