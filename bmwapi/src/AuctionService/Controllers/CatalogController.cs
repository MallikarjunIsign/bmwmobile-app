using Contracts;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Controllers;

/// <summary>
/// Master data (design doc §3, §8.1): the part catalogue (Category &gt; Sub-category &gt; Part type, with synonyms and
/// fitment modes) and makes / models with aliases. Sellers pick from these lists; a missing make or model can be
/// requested ("Other – request to add") – usable on the requester's listing right away, approved by an admin later.
/// </summary>
[ApiController]
[Route("api/catalog")]
public class CatalogController(AuctionDbContext context, IConfiguration configuration) : ControllerBase
{
    private string? Username => User.Identity?.Name;

    /// <summary>
    /// The whole part catalogue: categories, sub-categories and part types. commonOnly: only part types that fit any
    /// vehicle, and only the (sub-)categories that still contain some.
    /// </summary>
    [HttpGet("categories")]
    public async Task<ActionResult<List<CategoryDto>>> Categories([FromQuery] bool commonOnly = false)
    {
        var categories = await context.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync();
        var partTypes = await context.PartTypes.AsNoTracking().Include(p => p.Category).ThenInclude(c => c!.Parent)
            .Where(p => !commonOnly || p.FitmentMode != FitmentMode.VehicleSpecific)
            .OrderBy(p => p.Name).ToListAsync();

        CategoryDto Build(Category c) => new()
        {
            Id = c.Id,
            Name = c.Name,
            SubCategories = categories.Where(x => x.ParentId == c.Id).Select(Build)
                .Where(x => !commonOnly || x.PartTypes.Count + x.SubCategories.Count > 0).ToList(),
            PartTypes = partTypes.Where(p => p.CategoryId == c.Id).Select(p => ToDto(p)).ToList()
        };

        return categories.Where(c => c.ParentId == null).Select(Build)
            .Where(x => !commonOnly || x.PartTypes.Count + x.SubCategories.Count > 0).ToList();
    }

    /// <summary>
    /// Search-as-you-type over part types and their synonyms ("bonnet" → Hood, "dickey" → Boot lid).
    /// Names starting with the text come first.
    /// </summary>
    [HttpGet("part-types")]
    public async Task<ActionResult<List<PartTypeDto>>> PartTypes([FromQuery] string? q, [FromQuery] string? fitmentMode,
        [FromQuery] int limit = 20, [FromQuery] bool commonOnly = false)
    {
        var all = await context.PartTypes.AsNoTracking().Include(p => p.Category).ThenInclude(c => c!.Parent).ToListAsync();
        if (Enum.TryParse<FitmentMode>(fitmentMode, true, out var mode)) all = all.Where(p => p.FitmentMode == mode).ToList();
        // Common parts: only the types that fit any vehicle (universal or by size).
        if (commonOnly) all = all.Where(p => p.FitmentMode != FitmentMode.VehicleSpecific).ToList();

        var text = q?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text))
            return all.OrderBy(p => p.Name).Take(Math.Clamp(limit, 1, 200)).Select(p => ToDto(p)).ToList();

        return all
            .Select(p =>
            {
                var name = p.Name.ToLowerInvariant();
                var synonym = p.Synonyms.FirstOrDefault(s => s.StartsWith(text)) ?? p.Synonyms.FirstOrDefault(s => s.Contains(text));
                var score = name.StartsWith(text) ? 0
                    : name.Split(' ').Any(w => w.StartsWith(text)) ? 1
                    : synonym is not null && synonym.StartsWith(text) ? 2
                    : name.Contains(text) ? 3
                    : synonym is not null ? 4
                    : -1;
                return (Type: p, Score: score, Synonym: score >= 2 ? synonym : null);
            })
            .Where(x => x.Score >= 0)
            .OrderBy(x => x.Score).ThenBy(x => x.Type.Name.Length).ThenBy(x => x.Type.Name)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(x => ToDto(x.Type, x.Synonym))
            .ToList();
    }

    [HttpGet("part-types/{id:int}")]
    public async Task<ActionResult<PartTypeDto>> PartType(int id)
    {
        var p = await context.PartTypes.AsNoTracking().Include(x => x.Category).ThenInclude(c => c!.Parent)
            .FirstOrDefaultAsync(x => x.Id == id);
        return p is null ? NotFound() : ToDto(p);
    }

    /// <summary>
    /// Makes for the pickers: approved ones plus the caller's own pending requests. With country, the makes popular
    /// there come first. q filters by name or alias ("chevy" → Chevrolet).
    /// </summary>
    [HttpGet("makes")]
    public async Task<ActionResult<List<MakeDto>>> Makes([FromQuery] string? q, [FromQuery] string? country)
    {
        var user = Username;
        var makes = await context.Makes.AsNoTracking()
            .Where(m => m.IsApproved || (user != null && m.RequestedBy == user))
            .ToListAsync();
        var text = q?.Trim().ToLowerInvariant();
        var c = country?.ToUpperInvariant();

        return makes
            .Where(m => string.IsNullOrEmpty(text) || m.Name.ToLowerInvariant().Contains(text) || m.Aliases.Any(a => a.Contains(text)))
            .OrderBy(m => c != null && m.Countries.Contains(c) ? 0 : 1)
            .ThenBy(m => m.Name)
            .Select(ToDto)
            .ToList();
    }

    /// <summary>Models of a make (approved + the caller's pending requests).</summary>
    [HttpGet("makes/{makeId:int}/models")]
    public async Task<ActionResult<List<ModelDto>>> Models(int makeId, [FromQuery] string? q, [FromQuery] string? country)
    {
        var user = Username;
        var make = await context.Makes.AsNoTracking().FirstOrDefaultAsync(m => m.Id == makeId);
        if (make is null) return NotFound();

        var text = q?.Trim().ToLowerInvariant();
        var c = country?.ToUpperInvariant();
        var models = await context.Models.AsNoTracking()
            .Where(m => m.MakeId == makeId && (m.IsApproved || (user != null && m.RequestedBy == user)))
            .ToListAsync();

        return models
            .Where(m => string.IsNullOrEmpty(text) || m.Name.ToLowerInvariant().Contains(text) || m.Aliases.Any(a => a.Contains(text)))
            .OrderBy(m => c != null && m.Countries.Contains(c) ? 0 : 1).ThenBy(m => m.Name)
            .Select(m => ToDto(m, make.Name))
            .ToList();
    }

    /// <summary>Known trims / engines for a model (optional fitment fields).</summary>
    [HttpGet("models/{modelId:int}/configs")]
    public async Task<ActionResult<List<VehicleConfigDto>>> Configs(int modelId) =>
        await context.VehicleConfigs.AsNoTracking().Where(v => v.ModelId == modelId)
            .OrderBy(v => v.YearFrom).ThenBy(v => v.Trim)
            .Select(v => new VehicleConfigDto
            {
                YearFrom = v.YearFrom, YearTo = v.YearTo, Trim = v.Trim, Engine = v.Engine, Fuel = v.Fuel,
                Transmission = v.Transmission, Drivetrain = v.Drivetrain, Market = v.Market
            })
            .ToListAsync();

    /// <summary>Popular makes and models for a country (Marketplace pills when there are few live listings).</summary>
    [HttpGet("popular")]
    public async Task<ActionResult<object>> Popular([FromQuery] string? country, [FromQuery] int top = 10)
    {
        var c = (country ?? "IN").ToUpperInvariant();
        var makes = await context.Makes.AsNoTracking().Where(m => m.IsApproved && m.Countries.Contains(c))
            .OrderBy(m => m.Name).Take(Math.Clamp(top, 1, 50)).ToListAsync();
        var models = await context.Models.AsNoTracking().Include(m => m.Make)
            .Where(m => m.IsApproved && m.Countries.Contains(c))
            .OrderBy(m => m.Make!.Name).ThenBy(m => m.Name).Take(Math.Clamp(top, 1, 50)).ToListAsync();
        return Ok(new
        {
            makes = makes.Select(m => m.Name),
            models = models.Select(m => new { make = m.Make!.Name, model = m.Name })
        });
    }

    /// <summary>"Other – request to add" a make. Usable on the requester's listings at once; an admin approves it.</summary>
    [Authorize]
    [HttpPost("makes/request")]
    public async Task<ActionResult<MakeDto>> RequestMake(RequestMakeDto dto)
    {
        var name = Clean(dto.Name);
        if (name is null) return BadRequest(new { message = "Enter the make name" });

        var existing = await context.Makes.FirstOrDefaultAsync(m => m.Name.ToLower() == name.ToLower());
        if (existing is not null) return ToDto(existing);

        var make = new Make { Name = name, IsApproved = false, RequestedBy = Username };
        context.Makes.Add(make);
        await context.SaveChangesAsync();
        return ToDto(make);
    }

    /// <summary>"Other – request to add" a model (of an existing or a just-requested make).</summary>
    [Authorize]
    [HttpPost("models/request")]
    public async Task<ActionResult<ModelDto>> RequestModel(RequestModelDto dto)
    {
        var name = Clean(dto.Name);
        if (name is null) return BadRequest(new { message = "Enter the model name" });

        Make? make = dto.MakeId is not null
            ? await context.Makes.FirstOrDefaultAsync(m => m.Id == dto.MakeId)
            : Clean(dto.MakeName) is { } makeName
                ? await context.Makes.FirstOrDefaultAsync(m => m.Name.ToLower() == makeName.ToLower())
                : null;
        if (make is null) return BadRequest(new { message = "Choose or request the make first" });

        var existing = await context.Models.FirstOrDefaultAsync(m => m.MakeId == make.Id && m.Name.ToLower() == name.ToLower());
        if (existing is not null) return ToDto(existing, make.Name);

        var model = new VehicleModel { MakeId = make.Id, Name = name, IsApproved = false, RequestedBy = Username };
        context.Models.Add(model);
        await context.SaveChangesAsync();
        return ToDto(model, make.Name);
    }

    /// <summary>Admin: makes and models waiting for approval.</summary>
    [Authorize]
    [HttpGet("pending")]
    public async Task<ActionResult<object>> Pending()
    {
        if (!IsAdmin()) return Forbid();
        var makes = await context.Makes.AsNoTracking().Where(m => !m.IsApproved).ToListAsync();
        var models = await context.Models.AsNoTracking().Include(m => m.Make).Where(m => !m.IsApproved).ToListAsync();
        return Ok(new
        {
            makes = makes.Select(ToDto),
            models = models.Select(m => ToDto(m, m.Make!.Name))
        });
    }

    /// <summary>Admin: approve a requested make (it joins the lists for everyone).</summary>
    [Authorize]
    [HttpPost("makes/{id:int}/approve")]
    public async Task<IActionResult> ApproveMake(int id)
    {
        if (!IsAdmin()) return Forbid();
        var make = await context.Makes.FindAsync(id);
        if (make is null) return NotFound();
        make.IsApproved = true;
        await context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Admin: approve a requested model (approves its make too).</summary>
    [Authorize]
    [HttpPost("models/{id:int}/approve")]
    public async Task<IActionResult> ApproveModel(int id)
    {
        if (!IsAdmin()) return Forbid();
        var model = await context.Models.Include(m => m.Make).FirstOrDefaultAsync(m => m.Id == id);
        if (model is null) return NotFound();
        model.IsApproved = true;
        model.Make!.IsApproved = true;
        await context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>The words SearchService recognises: makes/models with aliases, part types with synonyms (approved only).</summary>
    [HttpGet("dictionary")]
    public async Task<ActionResult<SearchDictionaryDto>> Dictionary()
    {
        var makes = await context.Makes.AsNoTracking().Include(m => m.Models).Where(m => m.IsApproved).ToListAsync();
        var partTypes = await context.PartTypes.AsNoTracking().Include(p => p.Category).ThenInclude(c => c!.Parent).ToListAsync();

        return new SearchDictionaryDto
        {
            Makes = makes.Select(m => new DictionaryMake
            {
                Name = m.Name,
                Aliases = m.Aliases.ToList(),
                Countries = m.Countries.ToList(),
                Models = m.Models.Where(x => x.IsApproved).Select(x => new DictionaryModel
                {
                    Name = x.Name, Aliases = x.Aliases.ToList(), Countries = x.Countries.ToList()
                }).ToList()
            }).ToList(),
            PartTypes = partTypes.Select(p => new DictionaryPartType
            {
                Id = p.Id,
                Name = p.Name,
                Category = (p.Category?.Parent ?? p.Category)?.Name ?? "",
                FitmentMode = p.FitmentMode.ToString(),
                Synonyms = p.Synonyms.ToList()
            }).ToList()
        };
    }

    /// <summary>Admins: token role "admin", or listed by username in the "Admin:Usernames" setting.</summary>
    private bool IsAdmin() => AppRoles.IsAdmin(User) ||
        Username is { } user &&
        (configuration.GetSection("Admin:Usernames").Get<string[]>() ?? []).Contains(user, StringComparer.OrdinalIgnoreCase);

    private static string? Clean(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length > 60 ? v[..60] : v;
    }

    public static PartTypeDto ToDto(PartType p, string? matchedSynonym = null)
    {
        var top = p.Category?.Parent ?? p.Category;
        return new PartTypeDto
        {
            Id = p.Id,
            Name = p.Name,
            CategoryId = p.CategoryId,
            Category = top?.Name ?? "",
            SubCategory = p.Category?.Parent is null ? null : p.Category.Name,
            FitmentMode = p.FitmentMode.ToString(),
            HasPosition = p.HasPosition,
            HasSide = p.HasSide,
            IsBodyPart = p.IsBodyPart,
            Synonyms = p.Synonyms.ToList(),
            ImportantFitmentFields = p.ImportantFitmentFields.ToList(),
            SpecFields = p.SpecFields,
            DefaultSizeClass = ShippingRules.DefaultSizeClass(ListingKind.CommonPart, p.Name).ToString(),
            DefaultWeightLb = Package(p).WeightLb,
            DefaultLengthIn = Package(p).LengthIn,
            DefaultWidthIn = Package(p).WidthIn,
            DefaultHeightIn = Package(p).HeightIn,
            MatchedSynonym = matchedSynonym
        };
    }

    private static MakeDto ToDto(Make m) => new()
    {
        Id = m.Id, Name = m.Name, Aliases = m.Aliases.ToList(), Countries = m.Countries.ToList(), IsApproved = m.IsApproved
    };

    private static ModelDto ToDto(VehicleModel m, string make) => new()
    {
        Id = m.Id, MakeId = m.MakeId, Make = make, Name = m.Name, Aliases = m.Aliases.ToList(), BodyType = m.BodyType,
        IsApproved = m.IsApproved
    };

    private static Parcel Package(PartType p) => ParcelDefaults.For(p.Name, ShippingRules.DefaultSizeClass(ListingKind.CommonPart, p.Name));
}
