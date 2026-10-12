namespace ChatService.Services;

/// <summary>"Chat" settings: message limits and the rate limit.</summary>
public class ChatOptions
{
    public const string SectionName = "Chat";

    public int MaxTextLength { get; set; } = 1000;
    public int MaxPhotos { get; set; } = 5;
    /// <summary>Messages one user may send per minute (all conversations together).</summary>
    public int MessagesPerMinute { get; set; } = 30;
    public int PageSize { get; set; } = 50;
    /// <summary>
    /// Absolute photo URLs accepted besides the Gateway's "/images/…" paths, e.g. the Google Cloud Storage bucket
    /// ("https://storage.googleapis.com/my-bucket/"). Photos are always uploaded through AuctionService's /api/media.
    /// </summary>
    public string[] RemotePhotoPrefixes { get; set; } = [];
    /// <summary>Origin the app reaches "/images/…" photos on (the Gateway) – same as AuctionService's ImageStorage:PublicBaseUrl.</summary>
    public string PhotoBaseUrl { get; set; } = "http://localhost:6001";

    public string PhotoUrl(string stored) =>
        stored.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? stored : PhotoBaseUrl.TrimEnd('/') + stored;
}
