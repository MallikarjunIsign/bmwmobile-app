using AuctionService.Entities;

namespace AuctionService.DTOs;

public class PartTypeDto
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int CategoryId { get; set; }
    /// <summary>Top-level category ("Body – Exterior").</summary>
    public required string Category { get; set; }
    /// <summary>Sub-category, when the part type sits in one.</summary>
    public string? SubCategory { get; set; }
    /// <summary>Universal, SizeBased or VehicleSpecific.</summary>
    public required string FitmentMode { get; set; }
    public bool HasPosition { get; set; }
    public bool HasSide { get; set; }
    public bool IsBodyPart { get; set; }
    public List<string> Synonyms { get; set; } = [];
    public List<string> ImportantFitmentFields { get; set; } = [];
    public List<SpecFieldDefinition> SpecFields { get; set; } = [];
    /// <summary>The synonym that matched the search text, e.g. "bonnet" for Hood.</summary>
    public string? MatchedSynonym { get; set; }
}

public class CategoryDto
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<CategoryDto> SubCategories { get; set; } = [];
    public List<PartTypeDto> PartTypes { get; set; } = [];
}

public class MakeDto
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<string> Aliases { get; set; } = [];
    public List<string> Countries { get; set; } = [];
    /// <summary>False = requested by a seller, waiting for an admin.</summary>
    public bool IsApproved { get; set; }
}

public class ModelDto
{
    public int Id { get; set; }
    public int MakeId { get; set; }
    public required string Make { get; set; }
    public required string Name { get; set; }
    public List<string> Aliases { get; set; } = [];
    public string? BodyType { get; set; }
    public bool IsApproved { get; set; }
}

public class VehicleConfigDto
{
    public int YearFrom { get; set; }
    public int YearTo { get; set; }
    public string? Trim { get; set; }
    public string? Engine { get; set; }
    public string? Fuel { get; set; }
    public string? Transmission { get; set; }
    public string? Drivetrain { get; set; }
    public string? Market { get; set; }
}

public class RequestMakeDto
{
    public string Name { get; set; } = "";
}

public class RequestModelDto
{
    public int? MakeId { get; set; }
    public string? MakeName { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Everything SearchService needs to understand what buyers type (cached there).</summary>
public class SearchDictionaryDto
{
    public List<DictionaryMake> Makes { get; set; } = [];
    public List<DictionaryPartType> PartTypes { get; set; } = [];
}

public class DictionaryMake
{
    public required string Name { get; set; }
    public List<string> Aliases { get; set; } = [];
    public List<string> Countries { get; set; } = [];
    public List<DictionaryModel> Models { get; set; } = [];
}

public class DictionaryModel
{
    public required string Name { get; set; }
    public List<string> Aliases { get; set; } = [];
    public List<string> Countries { get; set; } = [];
}

public class DictionaryPartType
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public required string FitmentMode { get; set; }
    public List<string> Synonyms { get; set; } = [];
}
