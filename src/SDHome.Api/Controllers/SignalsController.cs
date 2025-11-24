using Microsoft.AspNetCore.Mvc;
using SDHome.Lib.Models;
using SDHome.Lib.Services;

namespace SDHome.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class  SignalsController(ISignalQueryService queryService) : ControllerBase
    {
        [HttpGet("logs")]
        public async Task<List<SignalEvent>> GetSignalLogs()
        {
            var res = await queryService.GetRecentAsync(take: 100);
            return [.. res];
        }
    }
}
