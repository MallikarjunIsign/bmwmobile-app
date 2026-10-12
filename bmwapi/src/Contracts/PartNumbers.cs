namespace Contracts;

/// <summary>Part numbers are stored as typed and normalised (uppercase, no spaces, dashes, dots or slashes).</summary>
public static class PartNumbers
{
    /// <summary>"52119-0k 935" → "521190K935". Null/blank → null.</summary>
    public static string? Normalize(string? partNumber)
    {
        if (string.IsNullOrWhiteSpace(partNumber)) return null;
        var chars = partNumber.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray();
        return chars.Length == 0 ? null : new string(chars);
    }
}
