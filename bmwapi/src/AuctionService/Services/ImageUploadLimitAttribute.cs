using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace AuctionService.Services;

/// <summary>
/// Raises the request body limit for an upload endpoint to MaxFiles x MaxFileSize (+1 MB multipart overhead).
/// A resource filter runs before model binding, which is when MVC first reads the form.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ImageUploadLimitAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var limits = context.HttpContext.RequestServices.GetRequiredService<IOptions<ImageUploadOptions>>().Value;
        var feature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();

        if (feature is { IsReadOnly: false })
            feature.MaxRequestBodySize = limits.MaxFiles * limits.MaxFileSizeBytes + 1024 * 1024;
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}