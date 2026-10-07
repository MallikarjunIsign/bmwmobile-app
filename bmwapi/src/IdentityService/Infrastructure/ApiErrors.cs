using IdentityService.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IdentityService.Infrastructure;

/// <summary>
/// Turns exceptions from the /api controllers into { "message": "...", "code": "..." }.
/// Applied to API controllers only, so the Razor login pages keep their normal error page.
/// </summary>
public class ApiExceptionFilter(ILogger<ApiExceptionFilter> logger, IHostEnvironment env) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is AppException app)
        {
            context.Result = new ObjectResult(new ErrorResponse(app.Message, app.Code)) { StatusCode = app.StatusCode };
        }
        else
        {
            logger.LogError(context.Exception, "Unhandled error on {Path}", context.HttpContext.Request.Path);
            var message = env.IsDevelopment() ? context.Exception.Message : "Something went wrong. Please try again.";
            context.Result = new ObjectResult(new ErrorResponse(message, "SERVER_ERROR"))
            {
                StatusCode = StatusCodes.Status500InternalServerError,
            };
        }
        context.ExceptionHandled = true;
    }
}

public static class ValidationResponse
{
    /// <summary>Model-validation errors in the same shape, with the first error as the main message.</summary>
    public static IActionResult Create(ActionContext context)
    {
        var errors = context.ModelState
            .Where(kv => kv.Value?.Errors.Count > 0)
            .ToDictionary(
                kv => kv.Key,
                kv => kv.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage).ToArray());

        var first = errors.Values.SelectMany(v => v).FirstOrDefault() ?? "Please check the details and try again.";
        return new BadRequestObjectResult(new ErrorResponse(first, "VALIDATION_FAILED", errors));
    }
}
