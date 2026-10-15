using System.Globalization;
using System.Text.RegularExpressions;
using Contracts;

namespace SearchService.Services;

/// <summary>A filter chip built from the query or a suggestion: [Front bumper] [Toyota] [Hilux] [2016].</summary>
public record QueryChip(string Type, string Value, string Label);

/// <summary>What the buyer typed, understood (design doc §7.1).</summary>
public class ParsedQuery
{
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public DictionaryPartType? PartType { get; set; }
    /// <summary>Front / Rear.</summary>
    public string? Position { get; set; }
    /// <summary>Left / Right.</summary>
    public string? Side { get; set; }
    /// <summary>Normalised part number (uppercase, letters and digits only).</summary>
    public string? PartNumber { get; set; }
    public string? PartNumberAsTyped { get; set; }
    /// <summary>Size / spec values, e.g. width=265, profile=65, rim=17.</summary>
    public Dictionary<string, string> Specs { get; set; } = [];
    /// <summary>The words that weren't recognised: normal (typo-tolerant) text search.</summary>
    public string Keywords { get; set; } = "";

    /// <summary>A copy (for counting facets with one filter left out).</summary>
    public ParsedQuery Copy()
    {
        var copy = (ParsedQuery)MemberwiseClone();
        copy.Specs = new Dictionary<string, string>(Specs);
        return copy;
    }

    public bool HasVehicle => Make is not null;
    public bool HasYear => YearFrom is not null;

    /// <summary>"toyota|hilux|2016" (or less when the year / model is missing).</summary>
    public string? VehicleKey(int? year = null)
    {
        if (Make is null) return null;
        var key = Models.Item.Key(Make);
        if (Model is null) return key;
        key += "|" + Models.Item.Key(Model);
        var y = year ?? YearFrom;
        return y is null ? key : $"{key}|{y}";
    }

    public string? TyreSize => Specs.TryGetValue("width", out var w) && Specs.TryGetValue("profile", out var p) && Specs.TryGetValue("rim", out var r)
        ? Models.Item.TyreSize(w, p, r)
        : null;

    /// <summary>"2016 Toyota Hilux", "Toyota Hilux 2016–2020", "Toyota".</summary>
    public string? VehicleLabel()
    {
        if (Make is null) return null;
        var years = YearFrom is null ? null : YearTo is not null && YearTo != YearFrom ? $"{YearFrom}–{YearTo}" : $"{YearFrom}";
        return string.Join(" ", new[] { years, Make, Model }.Where(x => !string.IsNullOrEmpty(x)));
    }

    public string? PartLabel()
    {
        if (PartType is null) return null;
        var position = string.Join(" ", new[] { Position, Side }.Where(x => x is not null));
        return position.Length > 0 ? $"{position} {Lower(PartType.Name)}" : PartType.Name;
    }

    private static string Lower(string name) => name.Length > 1 && name.All(c => !char.IsLetter(c) || char.IsUpper(c)) ? name : name.ToLowerInvariant();

    public List<QueryChip> Chips()
    {
        var chips = new List<QueryChip>();
        if (PartNumber is not null) chips.Add(new QueryChip("partNumber", PartNumber, PartNumberAsTyped ?? PartNumber));
        if (PartType is not null) chips.Add(new QueryChip("partType", PartType.Name, PartLabel()!));
        if (PartType is null && Position is not null) chips.Add(new QueryChip("position", Position, Position));
        if (TyreSize is { } size) chips.Add(new QueryChip("size", size, FormatTyre()));
        if (Make is not null) chips.Add(new QueryChip("make", Make, Make));
        if (Model is not null) chips.Add(new QueryChip("model", Model, Model));
        if (YearFrom is not null)
            chips.Add(new QueryChip("year", YearTo is not null && YearTo != YearFrom ? $"{YearFrom}-{YearTo}" : $"{YearFrom}",
                YearTo is not null && YearTo != YearFrom ? $"{YearFrom}–{YearTo}" : $"{YearFrom}"));
        return chips;
    }

    private string FormatTyre() => $"{Specs["width"]}/{Specs["profile"]} R{Specs["rim"]}";
}

/// <summary>
/// Understands search text: year / year range, make (or the make implied by the model), model, part type (incl.
/// synonyms and local words), position words (front, rear, left, right, LH, RH), part numbers and tyre sizes. The
/// words left over are normal keyword search.
/// </summary>
public static partial class QueryParser
{
    private static readonly HashSet<string> StopWords =
        ["for", "fits", "fit", "of", "the", "a", "an", "with", "and", "to", "in", "on", "car", "part", "parts", "my", "need", "want"];

    private static readonly Dictionary<string, (string? Position, string? Side)> PositionWords = new()
    {
        ["front"] = ("Front", null), ["rear"] = ("Rear", null), ["back"] = ("Rear", null),
        ["left"] = (null, "Left"), ["right"] = (null, "Right"), ["lh"] = (null, "Left"), ["rh"] = (null, "Right"),
        ["l"] = (null, null), ["r"] = (null, null), // never part numbers or keywords on their own
    };

    public static ParsedQuery Parse(string? text, SearchDictionary dictionary)
    {
        var result = new ParsedQuery();
        var work = (text ?? "").Trim();
        if (work.Length == 0) return result;

        // 1. Tyre size: 265/65 R17, 265/65R17, 265 65 17.
        var tyre = TyreRegex().Match(work);
        if (tyre.Success && IsTyre(tyre))
        {
            result.Specs["width"] = tyre.Groups[1].Value;
            result.Specs["profile"] = tyre.Groups[2].Value;
            result.Specs["rim"] = tyre.Groups[3].Value;
            work = work.Remove(tyre.Index, tyre.Length);
            result.PartType = dictionary.Data.PartTypes.FirstOrDefault(p => p.Name.Equals("Tyre", StringComparison.OrdinalIgnoreCase));
        }

        // 2. Year range, then a single year (1950 .. next year).
        var maxYear = DateTime.UtcNow.Year + 1;
        var range = YearRangeRegex().Match(work);
        if (range.Success && Year(range.Groups[1].Value, maxYear) is { } from && Year(range.Groups[2].Value, maxYear) is { } to && from <= to)
        {
            result.YearFrom = from;
            result.YearTo = to;
            work = work.Remove(range.Index, range.Length);
        }
        else
        {
            foreach (Match m in YearRegex().Matches(work))
            {
                if (Year(m.Value, maxYear) is not { } year) continue;
                result.YearFrom = result.YearTo = year;
                work = work.Remove(m.Index, m.Length);
                break;
            }
        }

        // 3. Part numbers written with dashes / dots ("52119-0K935").
        var rawTokens = work.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var token in rawTokens.ToList())
        {
            if (!token.Any(char.IsDigit) || !token.Any(c => c is '-' or '.' or '/')) continue;
            var normalized = PartNumbers.Normalize(token);
            if (normalized is { Length: >= 6 } && result.PartNumber is null)
            {
                result.PartNumber = normalized;
                result.PartNumberAsTyped = token;
                rawTokens.Remove(token);
            }
        }

        // 4. Dictionary phrases, longest first.
        var tokens = SearchDictionary.Normalize(string.Join(' ', rawTokens)).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var t in tokens)
            if (PositionWords.TryGetValue(t, out var pos))
            {
                result.Position ??= pos.Position;
                result.Side ??= pos.Side;
            }

        var used = new bool[tokens.Count];
        string? makeFromModel = null;
        for (var size = Math.Min(dictionary.MaxPhraseWords, tokens.Count); size >= 1; size--)
        {
            for (var i = 0; i + size <= tokens.Count; i++)
            {
                if (Enumerable.Range(i, size).Any(k => used[k])) continue;
                var phrase = string.Join(' ', tokens.Skip(i).Take(size));
                if (StopWords.Contains(phrase) || !dictionary.Phrases.TryGetValue(phrase, out var entries)) continue;
                if (!Apply(result, entries, phrase, ref makeFromModel)) continue;
                for (var k = i; k < i + size; k++) used[k] = true;
            }
        }
        if (result.Make is null && makeFromModel is not null) result.Make = makeFromModel;

        // 5. Leftovers: part number look-alikes ("52119 0k935" typed with a space counts too), then keywords.
        if (result.PartNumber is null)
        {
            for (var i = 0; i + 1 < tokens.Count && result.PartNumber is null; i++)
            {
                if (used[i] || used[i + 1] || !tokens[i].Any(char.IsDigit) || !tokens[i + 1].Any(char.IsDigit)) continue;
                var joined = tokens[i] + tokens[i + 1];
                if (!LooksLikePartNumber(joined) || Year(tokens[i], maxYear) is not null || Year(tokens[i + 1], maxYear) is not null) continue;
                result.PartNumber = PartNumbers.Normalize(joined);
                result.PartNumberAsTyped = $"{tokens[i]} {tokens[i + 1]}".ToUpperInvariant();
                used[i] = used[i + 1] = true;
            }
        }

        var keywords = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (used[i]) continue;
            var t = tokens[i];
            if (StopWords.Contains(t) || PositionWords.ContainsKey(t)) continue;
            if (result.PartNumber is null && LooksLikePartNumber(t))
            {
                result.PartNumber = PartNumbers.Normalize(t);
                result.PartNumberAsTyped = t.ToUpperInvariant();
                continue;
            }
            keywords.Add(t);
        }
        result.Keywords = string.Join(' ', keywords);
        return result;
    }

    /// <summary>Takes the first make / model / part type found; a model also gives its make (when unambiguous).</summary>
    private static bool Apply(ParsedQuery result, List<DictionaryEntry> entries, string phrase, ref string? makeFromModel)
    {
        var applied = false;

        var partType = entries.FirstOrDefault(e => e.Kind == "partType");
        if (partType is not null && result.PartType is null)
        {
            result.PartType = partType.PartType;
            applied = true;
        }

        var make = entries.FirstOrDefault(e => e.Kind == "make");
        if (make is not null && result.Make is null)
        {
            result.Make = make.Make;
            applied = true;
        }

        var models = entries.Where(e => e.Kind == "model").ToList();
        if (models.Count > 0 && result.Model is null)
        {
            // With a make already known, prefer that make's model; otherwise only when the model name is unique.
            var chosen = result.Make is not null
                ? models.FirstOrDefault(m => m.Make == result.Make)
                : models.Select(m => m.Make).Distinct().Count() == 1 ? models[0] : null;
            // Numeric model names ("406", "1500") only count together with their make.
            if (chosen is not null && phrase.All(char.IsDigit) && result.Make is null) chosen = null;
            if (chosen is not null)
            {
                result.Model = chosen.Model;
                if (result.Make is null) makeFromModel = chosen.Make;
                else if (result.Make != chosen.Make) result.Make = chosen.Make;
                applied = true;
            }
        }

        return applied;
    }

    /// <summary>"34116860912", "0K935X": at least 6 letters/digits with 3+ digits (not a year).</summary>
    public static bool LooksLikePartNumber(string token)
    {
        if (token.Length < 6) return false;
        var digits = token.Count(char.IsDigit);
        if (digits < 3) return false;
        return digits == token.Length ? token.Length >= 7 : token.All(char.IsLetterOrDigit);
    }

    private static int? Year(string value, int maxYear) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y >= 1950 && y <= maxYear ? y : null;

    private static bool IsTyre(Match m) =>
        int.TryParse(m.Groups[1].Value, out var w) && w is >= 125 and <= 355 &&
        int.TryParse(m.Groups[2].Value, out var p) && p is >= 25 and <= 95 &&
        int.TryParse(m.Groups[3].Value, out var r) && r is >= 10 and <= 24;

    [GeneratedRegex(@"\b(\d{3})\s*[/\s-]\s*(\d{2})\s*(?:z?r|[\s/-])\s*(\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex TyreRegex();

    [GeneratedRegex(@"\b(\d{4})\s*(?:-|–|to)\s*(\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex YearRangeRegex();

    [GeneratedRegex(@"\b\d{4}\b")]
    private static partial Regex YearRegex();
}
