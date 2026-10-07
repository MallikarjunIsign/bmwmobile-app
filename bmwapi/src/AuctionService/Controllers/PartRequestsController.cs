using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PartRequestsController(AuctionDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PartRequestDto>>> GetPartRequests()
    {
        var username = User.Identity?.Name ?? "demo_buyer";
        var requests = await context.PartRequests
            .AsNoTracking()
            .Where(x => x.User == username || username == "demo_buyer")
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return requests.Select(MapToDto).ToList();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PartRequestDto>> GetPartRequest(string id)
    {
        var req = await context.PartRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (req == null) return NotFound();
        return MapToDto(req);
    }

    [HttpPost]
    public async Task<ActionResult<PartRequestDto>> CreatePartRequest(CreatePartRequestDto dto)
    {
        var username = User.Identity?.Name ?? "demo_buyer";

        var req = new PartRequest
        {
            User = username,
            Make = dto.Make,
            Model = dto.Model,
            Year = dto.Year,
            PartName = dto.PartName,
            Vin = dto.Vin,
            Specs = dto.Specs,
            MaxBudget = dto.MaxBudget,
            PreferredPort = dto.PreferredPort ?? "Lagos Apapa",
            Status = "Feasibility Passed",
            FeasibilityNote = $"Matched with Container CN-2026-09. Cutoff in 12 days. Destination: {dto.PreferredPort ?? "Lagos Apapa"}.",
            EstimatedDeliveryDate = DateTime.UtcNow.AddDays(35)
        };

        context.PartRequests.Add(req);
        await context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPartRequest), new { id = req.Id }, MapToDto(req));
    }

    [HttpGet("check-feasibility")]
    public ActionResult<FeasibilityCheckResultDto> CheckFeasibility(string make, string model, int year, string partName)
    {
        var now = DateTime.UtcNow;
        return Ok(new FeasibilityCheckResultDto
        {
            IsFeasible = true,
            TargetContainerCode = "CN-2026-09",
            FeasibilityStatus = "Feasible & Slots Available",
            CutoffDate = now.AddDays(12),
            DaysRemaining = 12,
            PickupPort = "Lagos Apapa Port",
            Note = $"Part '{partName}' for {year} {make} {model} can be packed into container CN-2026-09 leaving Bremerhaven before cutoff date."
        });
    }

    private static PartRequestDto MapToDto(PartRequest r) => new()
    {
        Id = r.Id,
        User = r.User,
        Make = r.Make,
        Model = r.Model,
        Year = r.Year,
        PartName = r.PartName,
        Vin = r.Vin,
        Specs = r.Specs,
        MaxBudget = r.MaxBudget,
        PreferredPort = r.PreferredPort,
        Status = r.Status,
        FeasibilityNote = r.FeasibilityNote,
        EstimatedDeliveryDate = r.EstimatedDeliveryDate,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };
}
