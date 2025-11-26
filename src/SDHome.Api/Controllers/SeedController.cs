using Microsoft.AspNetCore.Mvc;
using SDHome.Lib.Services;

namespace SDHome.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SeedController(DatabaseSeeder seeder) : ControllerBase
{
    private readonly DatabaseSeeder _seeder = seeder;

    /// <summary>
    /// Seed the database with sample data
    /// </summary>
    [HttpPost]
    public async Task<ActionResult> SeedDatabase()
    {
        await _seeder.SeedAsync();
        return Ok(new { message = "Database seeded successfully" });
    }
}
