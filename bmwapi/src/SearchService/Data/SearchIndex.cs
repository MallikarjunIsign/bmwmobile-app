using Meilisearch;

namespace SearchService.Data;

public static class SearchIndex
{
    /// <summary>
    /// The auctions index. Renamed from "items" when the reserve price was removed from the documents
    /// (a partial update can't remove a field), so the new index is built from scratch on startup.
    /// </summary>
    public const string Name = "auctions";

    public static readonly string[] LegacyNames = ["items"];

    public static Meilisearch.Index Auctions(this MeilisearchClient client) => client.Index(Name);

    // Meilisearch filter values must be quoted; escape quotes/backslashes in user input.
    public static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}