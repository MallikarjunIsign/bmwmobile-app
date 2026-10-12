using AuctionService.Data;
using AuctionService.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Controllers;

public class GarageVehicleDto
{
    public string? Id { get; set; }
    public int Year { get; set; }
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public string? Trim { get; set; }
    public string? Engine { get; set; }
    public string? Nickname { get; set; }
}

/// <summary>"My Garage": the buyer's saved vehicles. Choosing one filters every search and Home by that vehicle.</summary>
[ApiController]
[Authorize]
[Route("api/garage")]
public class GarageController(AuctionDbContext context) : ControllerBase
{
    public const int MaxVehicles = 10;
    private string Username => User.Identity?.Name ?? throw new UnauthorizedAccessException();

    [HttpGet]
    public async Task<ActionResult<List<GarageVehicleDto>>> Get() =>
        (await context.GarageVehicles.AsNoTracking().Where(v => v.User == Username).OrderBy(v => v.CreatedAt).ToListAsync())
        .Select(ToDto).ToList();

    [HttpPost]
    public async Task<ActionResult<GarageVehicleDto>> Add(GarageVehicleDto dto)
    {
        var maxYear = DateTime.UtcNow.Year + 1;
        if (dto.Year < 1950 || dto.Year > maxYear) return BadRequest(new { message = $"Year must be between 1950 and {maxYear}" });
        if (string.IsNullOrWhiteSpace(dto.Make) || string.IsNullOrWhiteSpace(dto.Model))
            return BadRequest(new { message = "Choose the make and model" });
        if (await context.GarageVehicles.CountAsync(v => v.User == Username) >= MaxVehicles)
            return BadRequest(new { message = $"You can save up to {MaxVehicles} vehicles" });

        var vehicle = new GarageVehicle
        {
            User = Username,
            Year = dto.Year,
            Make = dto.Make.Trim(),
            Model = dto.Model.Trim(),
            Trim = string.IsNullOrWhiteSpace(dto.Trim) ? null : dto.Trim.Trim(),
            Engine = string.IsNullOrWhiteSpace(dto.Engine) ? null : dto.Engine.Trim(),
            Nickname = string.IsNullOrWhiteSpace(dto.Nickname) ? null : dto.Nickname.Trim()
        };
        context.GarageVehicles.Add(vehicle);
        await context.SaveChangesAsync();
        return ToDto(vehicle);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var vehicle = await context.GarageVehicles.FirstOrDefaultAsync(v => v.Id == id && v.User == Username);
        if (vehicle is null) return NotFound();
        context.GarageVehicles.Remove(vehicle);
        await context.SaveChangesAsync();
        return NoContent();
    }

    private static GarageVehicleDto ToDto(GarageVehicle v) => new()
    {
        Id = v.Id, Year = v.Year, Make = v.Make, Model = v.Model, Trim = v.Trim, Engine = v.Engine, Nickname = v.Nickname
    };
}
