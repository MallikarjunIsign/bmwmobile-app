using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuctionService.Entities;

[Table("ItemImages")]
public class ItemImage
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(36)]
    public required string ItemId { get; set; }
    // Either a path served by this service ("/images/{file}") or, for seed data only, an absolute URL.
    [MaxLength(500)]
    public required string Url { get; set; }
    [MaxLength(200)]
    public required string FileName { get; set; }
    /// <summary>Thumbnail (~400 px) path or URL; null for seed images.</summary>
    [MaxLength(500)]
    public string? ThumbnailUrl { get; set; }
    [MaxLength(200)]
    public string? ThumbnailFileName { get; set; }
    /// <summary>Vehicles: the guided photo angle (Front, Rear, Left, Right, Interior, Dashboard, EngineBay, Damage).</summary>
    [MaxLength(20)]
    public string? Angle { get; set; }
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Item? Item { get; set; }

    /// <summary>The file belongs to our image storage (local disk or a cloud bucket), so it is deleted/copied with the item.</summary>
    [NotMapped]
    public bool IsStored => !IsExternal || ThumbnailFileName != null;

    [NotMapped]
    public bool IsExternal => Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                              || Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}