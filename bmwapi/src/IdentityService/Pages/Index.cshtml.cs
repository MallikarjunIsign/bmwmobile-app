using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityService.Pages;

[AllowAnonymous]
public class Index : PageModel
{
    public Index() { }

    public string Version => typeof(Duende.IdentityServer.Hosting.IdentityServerMiddleware).Assembly
                                 .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                 ?.InformationalVersion.Split('+').First()
                             ?? "unavailable";
}



