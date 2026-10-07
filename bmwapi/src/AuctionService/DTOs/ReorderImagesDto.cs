namespace AuctionService.DTOs;

public class ReorderImagesDto
{
    /// <summary>Every image id of the item, in the new display order.</summary>
    public List<string> ImageIds { get; set; } = [];
    /// <summary>Optional; defaults to the first id in ImageIds.</summary>
    public string? PrimaryImageId { get; set; }
}