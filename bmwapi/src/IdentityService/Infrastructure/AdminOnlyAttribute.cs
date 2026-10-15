using Contracts;
using IdentityService.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IdentityService.Infrastructure;

/// <summary>
/// 403 with a friendly { message, code } unless the token's role is admin.
/// An action filter, so it always runs after [Authorize] has signed the user in (401 stays 401).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AdminOnlyAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!AppRoles.IsAdmin(context.HttpContext.User))
        {
            context.Result = new ObjectResult(new ErrorResponse(AppRoles.AdminOnlyMessage, AppRoles.AdminOnlyCode))
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
    }
}
