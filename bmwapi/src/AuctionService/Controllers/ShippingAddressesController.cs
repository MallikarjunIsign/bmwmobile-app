using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Controllers;

/// <summary>
/// The buyer's shipping address book: addresses used for Shipping orders, one of them the default (offered first).
/// Addresses are copied onto an order, so editing or deleting one never changes an existing order.
/// </summary>
[ApiController]
[Authorize]
[Route("api/shipping-addresses")]
public class ShippingAddressesController(AuctionDbContext context, TimeProvider clock) : ControllerBase
{
    public const int MaxAddresses = 20;

    private string Username => User.Identity?.Name ?? throw new UnauthorizedAccessException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>The caller's addresses, the default first.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ShippingAddressDto>>> Mine()
    {
        var list = await context.ShippingAddresses.AsNoTracking().Where(a => a.User == Username)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.UpdatedAt).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<ShippingAddressDto>> Add(ShippingAddressDto dto)
    {
        var errors = AddressRules.Validate(dto);
        if (errors.Count > 0) return Invalid(errors);
        if (await context.ShippingAddresses.CountAsync(a => a.User == Username) >= MaxAddresses)
            return BadRequest(new { message = $"You can save up to {MaxAddresses} addresses" });

        var saved = await AddToBookAsync(context, Username, AddressRules.ToInfo(dto), Now, dto.IsDefault);
        await context.SaveChangesAsync();
        return CreatedAtAction(nameof(Mine), ToDto(saved));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ShippingAddressDto>> Update(string id, ShippingAddressDto dto)
    {
        var saved = await context.ShippingAddresses.FirstOrDefaultAsync(a => a.Id == id && a.User == Username);
        if (saved is null) return NotFound();
        var errors = AddressRules.Validate(dto);
        if (errors.Count > 0) return Invalid(errors);

        saved.Address = AddressRules.ToInfo(dto);
        saved.UpdatedAt = Now;
        if (dto.IsDefault) await MakeDefaultAsync(saved);
        await context.SaveChangesAsync();
        return ToDto(saved);
    }

    [HttpPost("{id}/default")]
    public async Task<ActionResult<ShippingAddressDto>> SetDefault(string id)
    {
        var saved = await context.ShippingAddresses.FirstOrDefaultAsync(a => a.Id == id && a.User == Username);
        if (saved is null) return NotFound();
        await MakeDefaultAsync(saved);
        await context.SaveChangesAsync();
        return ToDto(saved);
    }

    /// <summary>Deleting the default makes the most recently used other address the default.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var saved = await context.ShippingAddresses.FirstOrDefaultAsync(a => a.Id == id && a.User == Username);
        if (saved is null) return NotFound();
        context.ShippingAddresses.Remove(saved);
        if (saved.IsDefault)
        {
            var next = await context.ShippingAddresses.Where(a => a.User == Username && a.Id != id)
                .OrderByDescending(a => a.UpdatedAt).FirstOrDefaultAsync();
            if (next is not null) next.IsDefault = true;
        }
        await context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Adds an address to the user's book (not saved yet). The same address again just becomes the most recent;
    /// the first address, or one asked to be, is the default.
    /// </summary>
    public static async Task<ShippingAddress> AddToBookAsync(AuctionDbContext context, string user, AddressInfo address, DateTime now,
        bool makeDefault = false)
    {
        var mine = await context.ShippingAddresses.Where(a => a.User == user).ToListAsync();
        var same = mine.FirstOrDefault(a => a.Address.OneLine().Equals(address.OneLine(), StringComparison.OrdinalIgnoreCase));
        var entry = same ?? new ShippingAddress { User = user, Address = address, CreatedAt = now };
        entry.UpdatedAt = now;
        if (same is null) context.ShippingAddresses.Add(entry);

        if (makeDefault || mine.Count == 0 || !mine.Any(a => a.IsDefault))
        {
            foreach (var other in mine) other.IsDefault = false;
            entry.IsDefault = true;
        }
        return entry;
    }

    private async Task MakeDefaultAsync(ShippingAddress address)
    {
        var others = await context.ShippingAddresses.Where(a => a.User == Username && a.Id != address.Id && a.IsDefault).ToListAsync();
        foreach (var other in others) other.IsDefault = false;
        address.IsDefault = true;
        address.UpdatedAt = Now;
    }

    private static ShippingAddressDto ToDto(ShippingAddress a) => new()
    {
        Id = a.Id, IsDefault = a.IsDefault, Name = a.Address.Name, Phone = a.Address.Phone, Line1 = a.Address.Line1,
        Line2 = a.Address.Line2, City = a.Address.City, State = a.Address.State, PlaceId = a.Address.PlaceId,
        Postcode = a.Address.Postcode, CountryCode = a.Address.CountryCode
    };

    private ActionResult Invalid(Dictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
            foreach (var message in messages)
                ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
