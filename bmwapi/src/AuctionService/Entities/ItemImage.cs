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
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Item? Item { get; set; }

    [NotMapped]
    public bool IsExternal => Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                              || Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}