using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace AuctionService.Entities;

/// <summary>How a part type fits vehicles (design doc §2).</summary>
public enum FitmentMode
{
    /// <summary>Fits any vehicle (dash cam, tool kit, car cover): no vehicle details.</summary>
    Universal = 0,
    /// <summary>Fits any vehicle that takes that size/spec (tyre, bulb, battery, wiper blade, oil).</summary>
    SizeBased = 1,
    /// <summary>Fits certain vehicles only (bumper, headlamp, gearbox, door ...): fitment rows.</summary>
    VehicleSpecific = 2
}

/// <summary>"What are you adding?" on "+ Sell".</summary>
public enum ListingKind
{
    /// <summary>Universal or size-based part (no vehicle details).</summary>
    CommonPart = 0,
    VehicleSpecificPart = 1,
    Vehicle = 2
}

/// <summary>Category &gt; Sub-category (two levels; ParentId null = top level).</summary>
[Table("Categories")]
public class Category
{
    public int Id { get; set; }
    [MaxLength(100)]
    public required string Name { get; set; }
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public int SortOrder { get; set; }
    /// <summary>The old PartCategory enum value this category maps to (kept filled for older clients).</summary>
    public PartCategory LegacyCategory { get; set; } = PartCategory.Other;
    public List<PartType> PartTypes { get; set; } = [];
}

/// <summary>One spec field a size-based part type needs, e.g. Tyre → width, profile, rim.</summary>
public class SpecFieldDefinition
{
    public required string Key { get; set; }
    public required string Label { get; set; }
    /// <summary>"number", "text" or "select".</summary>
    public string Type { get; set; } = "text";
    public string? Unit { get; set; }
    public List<string> Options { get; set; } = [];
    public bool Required { get; set; } = true;
}

/// <summary>Part type master data (Front bumper, Headlamp, Tyre, Dash cam ...).</summary>
[Table("PartTypes")]
public class PartType
{
    public int Id { get; set; }
    [MaxLength(100)]
    public required string Name { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public FitmentMode FitmentMode { get; set; }
    /// <summary>Front / Rear position applies (bumper, door, brake pads).</summary>
    public bool HasPosition { get; set; }
    /// <summary>Left / Right / Centre applies (headlamp, mirror, door).</summary>
    public bool HasSide { get; set; }
    /// <summary>Colour / paint code is asked (body panels).</summary>
    public bool IsBodyPart { get; set; }
    /// <summary>Other names, incl. local words (bonnet = hood, dickey = trunk ...). Lower case.</summary>
    public string[] Synonyms { get; set; } = [];
    /// <summary>Fitment fields shown as important for this type (Transmission, Drivetrain, SteeringSide ...).</summary>
    public string[] ImportantFitmentFields { get; set; } = [];

    [Column(TypeName = "jsonb")]
    public string SpecFieldsJson { get; set; } = "[]";

    [NotMapped]
    public List<SpecFieldDefinition> SpecFields
    {
        get => JsonSerializer.Deserialize<List<SpecFieldDefinition>>(SpecFieldsJson, JsonOptions) ?? [];
        set => SpecFieldsJson = JsonSerializer.Serialize(value, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

[Table("Makes")]
public class Make
{
    public int Id { get; set; }
    [MaxLength(60)]
    public required string Name { get; set; }
    /// <summary>Other names buyers type (Chevy, Merc, Beemer, VW). Lower case.</summary>
    public string[] Aliases { get; set; } = [];
    /// <summary>Countries where it is popular (IN, US, NG, IE), for pills and suggestions.</summary>
    public string[] Countries { get; set; } = [];
    /// <summary>False = "Other – request to add": usable on the requester's listing, waiting for an admin.</summary>
    public bool IsApproved { get; set; } = true;
    [MaxLength(100)]
    public string? RequestedBy { get; set; }
    public List<VehicleModel> Models { get; set; } = [];
}

[Table("Models")]
public class VehicleModel
{
    public int Id { get; set; }
    public int MakeId { get; set; }
    public Make? Make { get; set; }
    [MaxLength(60)]
    public required string Name { get; set; }
    public string[] Aliases { get; set; } = [];
    public string[] Countries { get; set; } = [];
    [MaxLength(40)]
    public string? BodyType { get; set; }
    public bool IsApproved { get; set; } = true;
    [MaxLength(100)]
    public string? RequestedBy { get; set; }
}

/// <summary>Known trims / engines of a model (for the optional fitment fields).</summary>
[Table("VehicleConfigs")]
public class VehicleConfig
{
    public int Id { get; set; }
    public int ModelId { get; set; }
    public VehicleModel? Model { get; set; }
    public int YearFrom { get; set; }
    public int YearTo { get; set; }
    [MaxLength(60)]
    public string? Trim { get; set; }
    [MaxLength(60)]
    public string? Engine { get; set; }
    [MaxLength(20)]
    public string? Fuel { get; set; }
    [MaxLength(20)]
    public string? Transmission { get; set; }
    [MaxLength(20)]
    public string? Drivetrain { get; set; }
    [MaxLength(20)]
    public string? Market { get; set; }
}
