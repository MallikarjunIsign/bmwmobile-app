using Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AuctionService.Errors;

/// <summary>
/// Only seller accounts (token role "seller") may create, edit, publish, cancel or relist listings and manage photos.
/// Everyone else gets 403 { message, code: "SELLER_ONLY" }. An action filter, so [Authorize] has already run (401 stays 401).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SellerOnlyAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!AppRoles.IsSeller(context.HttpContext.User))
            context.Result = Forbidden.With(AppRoles.SellerOnlyMessage, AppRoles.SellerOnlyCode);
    }
}

/// <summary>403 with a message the app can show (instead of an empty Forbid()).</summary>
public static class Forbidden
{
    public static ObjectResult With(string message, string code = "FORBIDDEN") =>
        new(new { message, code }) { StatusCode = StatusCodes.Status403Forbidden };
}
