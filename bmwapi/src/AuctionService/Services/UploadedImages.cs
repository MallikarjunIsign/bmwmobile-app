namespace AuctionService.Services;

/// <summary>
/// Reads uploaded image files from a multipart request and checks each one by its real content (not the extension),
/// size and type before anything is stored. Shared by listing photos and chat / review photos.
/// </summary>
public static class UploadedImages
{
    public record Accepted(byte[] Data, string Extension);

    public record Result(IFormCollection? Form, List<Accepted> Files, string? Error);

    public static async Task<Result> ReadAsync(HttpRequest request, ImageUploadOptions limits, CancellationToken cancellationToken)
    {
        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (Exception e) when (e is InvalidDataException or BadHttpRequestException)
        {
            return new Result(null, [], $"Upload is too large: max {limits.MaxFileSizeMB} MB per photo");
        }

        if (form.Files.Count == 0) return new Result(form, [], "No files were uploaded");

        // Validate every file before storing any of them.
        var accepted = new List<Accepted>();
        foreach (var file in form.Files)
        {
            var name = Path.GetFileName(file.FileName);

            if (file.Length == 0) return new Result(form, [], $"{name} is empty");
            if (file.Length > limits.MaxFileSizeBytes)
                return new Result(form, [], $"{name} is larger than {limits.MaxFileSizeMB} MB");

            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (extension.Length > 0 && !limits.AllowedExtensions.Contains(extension))
                return new Result(form, [], $"{name} is not an allowed type ({string.Join(", ", limits.AllowedExtensions)})");

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, cancellationToken);
            var data = buffer.ToArray();

            // The content decides the type (and the stored extension), not the uploaded name.
            var detected = ImageSignature.Detect(data.AsSpan(0, Math.Min(data.Length, ImageSignature.HeaderLength)));
            if (detected == null || !IsAllowed(detected, limits))
                return new Result(form, [], $"{name} is not a valid {string.Join(", ", limits.AllowedExtensions)} image");

            accepted.Add(new Accepted(data, detected));
        }

        return new Result(form, accepted, null);
    }

    private static bool IsAllowed(string detectedExtension, ImageUploadOptions limits) =>
        limits.AllowedExtensions.Contains(detectedExtension) ||
        detectedExtension == ".jpg" && limits.AllowedExtensions.Contains(".jpeg");
}
