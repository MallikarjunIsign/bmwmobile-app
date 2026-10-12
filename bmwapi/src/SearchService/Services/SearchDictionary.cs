using System.Text.RegularExpressions;

namespace SearchService.Services;

public class DictionaryMake
{
    public string Name { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public List<string> Countries { get; set; } = [];
    public List<DictionaryModel> Models { get; set; } = [];
}

public class DictionaryModel
{
    public string Name { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public List<string> Countries { get; set; } = [];
}

public class DictionaryPartType
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string FitmentMode { get; set; } = "";
    public List<string> Synonyms { get; set; } = [];
}

/// <summary>AuctionService's GET /api/catalog/dictionary.</summary>
public class DictionaryData
{
    public List<DictionaryMake> Makes { get; set; } = [];
    public List<DictionaryPartType> PartTypes { get; set; } = [];
}

/// <summary>What a phrase in the dictionary stands for.</summary>
public record DictionaryEntry(string Kind, string Make, string? Model, DictionaryPartType? PartType);

/// <summary>
/// The words search understands (design doc §7.1): make names and aliases, model names and aliases, part-type names and
/// synonyms. Phrases are normalised (lower case, punctuation → space) so "F-150", "f 150" and "f150" all match.
/// </summary>
public partial class SearchDictionary
{
    public DictionaryData Data { get; }
    /// <summary>Normalised phrase → what it can mean (a model name may exist for several makes).</summary>
    public IReadOnlyDictionary<string, List<DictionaryEntry>> Phrases { get; }
    public int MaxPhraseWords { get; }

    public SearchDictionary(DictionaryData data)
    {
        Data = data;
        var phrases = new Dictionary<string, List<DictionaryEntry>>();
        void Add(string phrase, DictionaryEntry entry)
        {
            foreach (var key in Variants(phrase))
            {
                if (!phrases.TryGetValue(key, out var list)) phrases[key] = list = [];
                if (!list.Contains(entry)) list.Add(entry);
            }
        }

        foreach (var make in data.Makes)
        {
            var entry = new DictionaryEntry("make", make.Name, null, null);
            Add(make.Name, entry);
            foreach (var alias in make.Aliases) Add(alias, entry);
            foreach (var model in make.Models)
            {
                var modelEntry = new DictionaryEntry("model", make.Name, model.Name, null);
                Add(model.Name, modelEntry);
                foreach (var alias in model.Aliases) Add(alias, modelEntry);
                // "toyota hilux" as one phrase too.
                Add($"{make.Name} {model.Name}", modelEntry);
            }
        }

        foreach (var type in data.PartTypes)
        {
            var entry = new DictionaryEntry("partType", "", null, type);
            Add(type.Name, entry);
            foreach (var synonym in type.Synonyms) Add(synonym, entry);
        }

        Phrases = phrases;
        MaxPhraseWords = phrases.Keys.Select(k => k.Split(' ').Length).DefaultIfEmpty(1).Max();
    }

    public static SearchDictionary Empty { get; } = new(new DictionaryData());

    /// <summary>"F-150" → "f 150" and "f150"; "Wagon R" → "wagon r" and "wagonr".</summary>
    public static IEnumerable<string> Variants(string phrase)
    {
        var normalized = Normalize(phrase);
        if (normalized.Length == 0) yield break;
        yield return normalized;
        var joined = normalized.Replace(" ", "");
        if (joined != normalized && joined.Length >= 3) yield return joined;
    }

    public static string Normalize(string text) =>
        Spaces().Replace(NonWord().Replace(text.ToLowerInvariant(), " "), " ").Trim();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Meilisearch synonyms: every part-type synonym ↔ its name, every alias ↔ its make / model.</summary>
    public Dictionary<string, IEnumerable<string>> MeiliSynonyms()
    {
        var map = new Dictionary<string, HashSet<string>>();
        void Link(string a, string b)
        {
            a = a.ToLowerInvariant().Trim();
            b = b.ToLowerInvariant().Trim();
            if (a.Length == 0 || b.Length == 0 || a == b) return;
            if (!map.TryGetValue(a, out var set)) map[a] = set = [];
            set.Add(b);
        }

        foreach (var type in Data.PartTypes)
            foreach (var s in type.Synonyms)
            {
                Link(s, type.Name);
                Link(type.Name, s);
            }
        foreach (var make in Data.Makes)
        {
            foreach (var a in make.Aliases) Link(a, make.Name);
            foreach (var model in make.Models)
                foreach (var a in model.Aliases) Link(a, model.Name);
        }

        return map.ToDictionary(x => x.Key, x => (IEnumerable<string>)x.Value);
    }
}

/// <summary>
/// The current dictionary, loaded from AuctionService at startup and refreshed every few minutes
/// (new part types, approved makes/models).
/// </summary>
public class SearchDictionaryProvider
{
    private volatile SearchDictionary _current = SearchDictionary.Empty;
    public SearchDictionary Current => _current;
    public DateTime? LoadedAt { get; private set; }

    public void Set(DictionaryData data)
    {
        _current = new SearchDictionary(data);
        LoadedAt = DateTime.UtcNow;
    }
}
