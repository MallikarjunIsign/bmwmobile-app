using System.Globalization;
using Duende.IdentityServer;
using Duende.IdentityServer.EntityFramework.Storage;
using Duende.IdentityServer.EntityFramework.Stores;
using IdentityService.Data;
using IdentityService.Grants;
using IdentityService.Infrastructure;
using IdentityService.Models;
using IdentityService.Options;
using IdentityService.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Filters;

namespace IdentityService;

internal static class HostingExtensions
{
    public static WebApplicationBuilder ConfigureLogging(this WebApplicationBuilder builder)
    {
        // Write most logs to the console but diagnostic data to a file.
        // See https://duende.link/diagnostics
        _ = builder.Services.AddSerilog(lc =>
        {
            _ = lc.WriteTo.Logger(consoleLogger =>
            {
                _ = consoleLogger.WriteTo.Console(
                    outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}",
                    formatProvider: CultureInfo.InvariantCulture);
                if (builder.Environment.IsDevelopment())
                {
                    _ = consoleLogger.Filter.ByExcluding(
                        Matching.FromSource("Duende.IdentityServer.Diagnostics.Summary"));
                }
            });
            if (builder.Environment.IsDevelopment())
            {
                _ = lc.WriteTo.Logger(fileLogger =>
                {
                    _ = fileLogger
                        .WriteTo.File("./diagnostics/diagnostic.log", rollingInterval: RollingInterval.Day,
                            fileSizeLimitBytes: 1024 * 1024 * 10, // 10 MB
                            rollOnFileSizeLimit: true,
                            outputTemplate:
                            "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}",
                            formatProvider: CultureInfo.InvariantCulture)
                        .Filter
                        .ByIncludingOnly(Matching.FromSource("Duende.IdentityServer.Diagnostics.Summary"));
                }).Enrich.FromLogContext().ReadFrom.Configuration(builder.Configuration);
            }
        });
        return builder;
    }

    public static WebApplication ConfigureServices(this WebApplicationBuilder builder)
    {
        _ = builder.Services.AddRazorPages();

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        _ = builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Mobile app sign-up / login (OTP, trusted devices, Google / Apple). See Controllers/AccountController and Grants/.
        _ = builder.Services.Configure<OtpOptions>(builder.Configuration.GetSection("Otp"));
        _ = builder.Services.Configure<TrustedDeviceOptions>(builder.Configuration.GetSection("TrustedDevice"));
        _ = builder.Services.Configure<FeatureOptions>(builder.Configuration.GetSection("Features"));
        _ = builder.Services.Configure<SocialAuthOptions>(builder.Configuration.GetSection("SocialAuth"));
        _ = builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
        _ = builder.Services.Configure<LocationOptions>(builder.Configuration.GetSection("Location"));
        _ = builder.Services.AddScoped<AccountService>();
        _ = builder.Services.AddScoped<LocationService>();
        _ = builder.Services.AddScoped<IOtpService, OtpService>();
        _ = builder.Services.AddScoped<ITrustedDeviceService, TrustedDeviceService>();
        _ = builder.Services.AddSingleton<IOtpSender, EmailOtpSender>();
        _ = builder.Services.AddSingleton<ISocialTokenValidator, SocialTokenValidator>();
        _ = builder.Services.AddSingleton<ILoginChallenges, LoginChallenges>();
        _ = builder.Services
            .AddControllers()
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ValidationResponse.Create);
        _ = builder.Services.AddAccountRateLimits();

        // Refresh tokens and other grants are kept in Postgres so "Remember me" survives a restart.
        _ = builder.Services.AddOperationalDbContext(options =>
            options.ConfigureDbContext = b => b.UseNpgsql(connectionString,
                sql => sql.MigrationsAssembly(typeof(HostingExtensions).Assembly.GetName().Name)));

        _ = builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        _ = builder.Services
            .AddIdentityServer(options =>
            {
                options.Events.RaiseErrorEvents = true;
                options.Events.RaiseInformationEvents = true;
                options.Events.RaiseFailureEvents = true;
                options.Events.RaiseSuccessEvents = true;
                options.IssuerUri = builder.Configuration["IdentityServer:IssuerUri"];

                // Use a large chunk size for diagnostic data in development where it will be redirected to a local file.
                if (builder.Environment.IsDevelopment())
                {
                    options.Diagnostics.ChunkSize = 1024 * 1024 * 10; // 10 MB
                }
            })
            .AddInMemoryIdentityResources(Config.IdentityResources)
            .AddInMemoryApiScopes(Config.ApiScopes)
            .AddInMemoryClients(Config.Clients(builder.Configuration))
            .AddAspNetIdentity<ApplicationUser>()
            .AddLicenseSummary()
            .AddProfileService<CustomProfileService>()
            .AddPersistedGrantStore<PersistedGrantStore>()
            .AddExtensionGrantValidator<PasswordOtpGrantValidator>()
            .AddExtensionGrantValidator<OtpGrantValidator>()
            .AddExtensionGrantValidator<ExternalGrantValidator>();

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.SameSite = SameSiteMode.Lax;
        });

        _ = builder.Services.AddAuthentication();

        // Lets /api/account/me accept this server's own access tokens (scope IdentityServerApi).
        _ = builder.Services.AddLocalApiAuthentication();

        // add `.PersistKeysToâ€¦()` and `.ProtectKeysWithâ€¦()` calls
        // see more at https://docs.duendesoftware.com/general/data-protection
        _ = builder.Services.AddDataProtection()
            .SetApplicationName("IdentityServer");

        return builder.Build();
    }

    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        _ = app.UseSerilogRequestLogging();

        if (app.Environment.IsDevelopment())
        {
            _ = app.UseDeveloperExceptionPage();
        }

        _ = app.UseStaticFiles();
        _ = app.UseRouting();
        _ = app.UseRateLimiter();
        _ = app.UseIdentityServer();
        _ = app.UseAuthorization();

        _ = app.MapRazorPages()
            .RequireAuthorization();
        _ = app.MapControllers();

        return app;
    }
}


