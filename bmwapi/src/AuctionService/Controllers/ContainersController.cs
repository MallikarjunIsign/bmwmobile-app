using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.RequestHelpers;
using AuctionService.Validation;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace AuctionService.Controllers;

[ApiController]
[Route("api/[controller]")] // http://localhost:7001/api/containers
public class ContainersController(AuctionDbContext context, IDbContextOutbox<AuctionDbContext> outbox,
    AuctionMapper mapper) : ControllerBase
{
    /// <summary>Lists containers, optionally filtered by a search term (number, ports, countries).</summary>
    [HttpGet]
    public async Task<ActionResult<List<ContainerDto>>> GetContainers(string? searchTerm)
    {
        var query = context.Containers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = $"%{searchTerm.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.ContainerNumber, term) ||
                EF.Functions.ILike(x.OriginPort, term) || EF.Functions.ILike(x.OriginCountry, term) ||
                EF.Functions.ILike(x.DestinationPort, term) || EF.Functions.ILike(x.DestinationCountry, term));
        }

        return await query
            .OrderBy(x => x.EstimatedArrivalDate)
            .Select(x => new ContainerDto
            {
                Id = x.Id,
                ContainerNumber = x.ContainerNumber,
                OriginPort = x.OriginPort,
                OriginCountry = x.OriginCountry,
                DestinationPort = x.DestinationPort,
                DestinationCountry = x.DestinationCountry,
                DepartureDate = x.DepartureDate,
                EstimatedArrivalDate = x.EstimatedArrivalDate,
                ShippingStatus = x.ShippingStatus.ToString(),
                CreatedBy = x.CreatedBy,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                AuctionCount = x.Items.Count
            })
            .ToListAsync();
    }

    /// <summary>Lists upcoming planned container schedules for customs, part ordering, and container feasibility.</summary>
    [HttpGet("planned")]
    public ActionResult<List<PlannedContainerScheduleDto>> GetPlannedSchedules()
    {
        var now = DateTime.UtcNow;
        var schedules = new List<PlannedContainerScheduleDto>
        {
            new()
            {
                Id = "cnt-schedule-01",
                Code = "CN-2026-09",
                OriginPort = "Bremerhaven Port",
                OriginCountry = "Germany",
                DestinationPort = "Lagos Apapa Port",
                DestinationCountry = "Nigeria",
                CutoffDate = now.AddDays(12),
                DaysRemaining = 12,
                DepartureDate = now.AddDays(18),
                EstimatedArrivalDate = now.AddDays(35),
                CapacityPercentage = 68,
                Status = "Receiving Parts"
            },
            new()
            {
                Id = "cnt-schedule-02",
                Code = "CN-2026-10",
                OriginPort = "Munich Logistics Hub",
                OriginCountry = "Germany",
                DestinationPort = "Lagos Tin Can Island",
                DestinationCountry = "Nigeria",
                CutoffDate = now.AddDays(26),
                DaysRemaining = 26,
                DepartureDate = now.AddDays(32),
                EstimatedArrivalDate = now.AddDays(49),
                CapacityPercentage = 42,
                Status = "Receiving Parts"
            },
            new()
            {
                Id = "cnt-schedule-03",
                Code = "CN-2026-11",
                OriginPort = "Hamburg Sea Freight Center",
                OriginCountry = "Germany",
                DestinationPort = "Port Harcourt Onne Port",
                DestinationCountry = "Nigeria",
                CutoffDate = now.AddDays(40),
                DaysRemaining = 40,
                DepartureDate = now.AddDays(46),
                EstimatedArrivalDate = now.AddDays(63),
                CapacityPercentage = 15,
                Status = "Scheduled"
            }
        };

        return Ok(schedules);
    }

    /// <summary>A container with the auctions shipped in it.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ContainerDetailsDto>> GetContainer(string id)
    {
        var container = await context.Containers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (container == null) return NotFound();

        var auctions = await context.Auctions.AsNoTracking().WithDetails()
            .Where(x => x.Item.ContainerId == id)
            .OrderBy(x => x.AuctionEnd)
            .ToListAsync();

        var dto = ToDetails(container, auctions.Select(x => mapper.ToDto(x)).ToList());
        return dto;
    }

    [Authorize(Policy = ContainerRules.ManagePolicy)]
    [HttpPost]
    public async Task<ActionResult<ContainerDto>> CreateContainer(SaveContainerDto dto)
    {
        var invalid = Validate(dto);
        if (invalid != null) return invalid;

        var number = ContainerRules.NormalizeNumber(dto.ContainerNumber);
        if (await context.Containers.AnyAsync(x => x.ContainerNumber == number))
            return Conflict($"Container {number} already exists");

        var container = new Container
        {
            ContainerNumber = number,
            OriginPort = "",
            OriginCountry = "",
            DestinationPort = "",
            DestinationCountry = "",
            CreatedBy = User.Identity?.Name ?? throw new Exception("User has not been authenticated")
        };
        ContainerRules.Apply(dto, container);

        context.Containers.Add(container);
        await context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetContainer), new { id = container.Id }, ToDetails(container, []));
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateContainer(string id, SaveContainerDto dto)
    {
        var container = await context.Containers.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (container == null) return NotFound();
        if (!ContainerRules.CanEdit(container, User)) return Forbid();

        var invalid = Validate(dto);
        if (invalid != null) return invalid;

        var number = ContainerRules.NormalizeNumber(dto.ContainerNumber);
        if (await context.Containers.AnyAsync(x => x.ContainerNumber == number && x.Id != id))
            return Conflict($"Container {number} already exists");

        ContainerRules.Apply(dto, container);
        container.UpdatedAt = DateTime.UtcNow;

        // Bump the linked auctions too, so SearchService's startup catch-up also picks up the change.
        var auctionIds = container.Items.Select(x => x.Id).ToList();
        await context.Auctions.Where(x => auctionIds.Contains(x.Id))
            .ForEachAsync(x => x.UpdatedAt = container.UpdatedAt);

        var message = new ContainerUpdated
        {
            Id = container.Id,
            ContainerNumber = container.ContainerNumber,
            OriginPort = container.OriginPort,
            OriginCountry = container.OriginCountry,
            DestinationPort = container.DestinationPort,
            DestinationCountry = container.DestinationCountry,
            DepartureDate = container.DepartureDate,
            EstimatedArrivalDate = container.EstimatedArrivalDate,
            ShippingStatus = container.ShippingStatus.ToString(),
            AuctionIds = auctionIds
        };

        await outbox.PublishAsync(message);
        await outbox.SaveChangesAndFlushMessagesAsync();

        return NoContent();
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteContainer(string id)
    {
        var container = await context.Containers.FirstOrDefaultAsync(x => x.Id == id);
        if (container == null) return NotFound();
        if (!ContainerRules.CanEdit(container, User)) return Forbid();

        if (await context.Auctions.AnyAsync(x => x.Item.ContainerId == id))
            return Conflict("Cannot delete a container that is used by auctions");

        context.Containers.Remove(container);
        await context.SaveChangesAsync();

        return NoContent();
    }

    private ActionResult? Validate(SaveContainerDto dto)
    {
        var errors = ContainerRules.Validate(dto);
        if (errors.Count == 0) return null;

        foreach (var (field, messages) in errors)
            foreach (var message in messages)
                ModelState.AddModelError(field, message);

        return ValidationProblem(ModelState);
    }

    private static ContainerDetailsDto ToDetails(Container x, List<AuctionDto> auctions) => new()
    {
        Id = x.Id,
        ContainerNumber = x.ContainerNumber,
        OriginPort = x.OriginPort,
        OriginCountry = x.OriginCountry,
        DestinationPort = x.DestinationPort,
        DestinationCountry = x.DestinationCountry,
        DepartureDate = x.DepartureDate,
        EstimatedArrivalDate = x.EstimatedArrivalDate,
        ShippingStatus = x.ShippingStatus.ToString(),
        CreatedBy = x.CreatedBy,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt,
        AuctionCount = auctions.Count,
        Auctions = auctions
    };
}