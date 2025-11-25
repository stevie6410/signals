using Microsoft.AspNetCore.Mvc;
using SDHome.Lib.Models;
using SDHome.Lib.Services;

namespace SDHome.Api.Controllers
{
    [ApiController]
    [Route("/api/signals")]
    public class  SignalsController(ISignalQueryService queryService) : ControllerBase
    {
        [HttpGet("logs")]
        public async Task<List<SignalEvent>> GetSignalLogs([FromQuery] int take = 100)
        {
            var res = await queryService.GetRecentAsync(take);
            return [.. res];
        }
    }
}

