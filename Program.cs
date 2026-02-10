#region

using System.Reflection;
using System.Security.Claims;
using AGC_Management.Controller;
using AGC_Management.Services;
using AGC_Management.Utils;
using BlazorBootstrap;
using Blazorise;
using Blazorise.Bootstrap;
using Blazorise.Bootstrap5;
using DisCatSharp;
using DisCatSharp.Entities;
using Discord.OAuth2;
using KawaiiAPI.NET;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Logging;
using Npgsql;
using Sentry;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using ILogger = Serilog.ILogger;
using Log = Serilog.Log;

#endregion

namespace AGC_Management;

public class CurrentApplication
{
    public static string VersionString { get; set; } = GetVersionString();
    public static DiscordClient DiscordClient { get; set; }
    public static DiscordGuild TargetGuild { get; set; }
    public static ILogger Logger { get; set; }
    public static IServiceProvider ServiceProvider { get; set; }
    public static string BotPrefix { get; set; }
    public static HttpClient HttpClient { get; set; }

    private static string GetVersionString()
    {
        try
        {
            var version = typeof(Program)
                .Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            Console.Out.WriteLineAsync(version);
            
            if (!string.IsNullOrEmpty(version))
            {
                if (version.StartsWith("v"))
                {
                    return version;
                }
                try
                {
                    if (Logger != null)
                    {
                        Logger.Warning($"Version string '{version}' doesn't follow the expected format (should start with 'v')");
                    }
                }
                catch
                {
                }

                return version;
            }

            try
            {
                string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                return $"0.0.1-nightly.{timestamp}";
            }
            catch (Exception ex)
            {
                try
                {
                    if (Logger != null)
                    {
                        Logger.Error(ex, "Failed to generate timestamp for version string");
                    }
                }
                catch
                {
                }

                return "0.0.1-nightly.unknown";
            }
        }
        catch (Exception ex)
        {
            try
            {
                if (Logger != null)
                {
                    Logger.Error(ex, "Failed to determine version string");
                }
            }
            catch
            {
            }

            return "0.0.1-unknown";
        }
    }
}

internal class Program : BaseCommandModule
{
    private static void Main(string[] args)
    {
        MainAsync().GetAwaiter().GetResult();
    }

    private static async Task MainAsync()
    {
        CurrentApplication.HttpClient = new HttpClient();
        CurrentApplication.HttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                                                                              "(KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

        LogEventLevel loglevel;
        try
        {
            loglevel = bool.Parse(BotConfig.GetConfig()["MainConfig"]["VerboseLogging"])
                ? LogEventLevel.Debug
                : LogEventLevel.Information;
        }
        catch
        {
            loglevel = LogEventLevel.Information;
        }

        var builder = WebApplication.CreateBuilder();
        var logger = Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(loglevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Discord.OAuth2", LogEventLevel.Warning)
            .WriteTo.Console()
            // errors to errorfile
            .WriteTo.File("logs/errors/error-.txt", rollingInterval: RollingInterval.Day,
                levelSwitch: new LoggingLevelSwitch(LogEventLevel.Error))
            .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day, levelSwitch: new LoggingLevelSwitch())
            .CreateLogger();
        CurrentApplication.Logger = logger;


        logger.Information("Starting AGC Management Bot " + CurrentApplication.VersionString + "...");
        bool DebugMode;
        try
        {
            DebugMode = bool.Parse(BotConfig.GetConfig()["MainConfig"]["DebugMode"]);
        }
        catch
        {
            DebugMode = false;
        }

        if (!DebugMode)
        {
            SentrySdk.Init(o =>
            {
                o.Dsn = BotConfig.GetConfig()["MainConfig"]["SentryDSN"];
                o.Debug = true;
                o.AutoSessionTracking = true;
                o.IsGlobalModeEnabled = true;
            });
        }

        string DcApiToken = "";
        try
        {
            DcApiToken = DebugMode
                ? BotConfig.GetConfig()["MainConfig"]["Discord_API_Token_DEB"]
                : BotConfig.GetConfig()["MainConfig"]["Discord_API_Token"];
        }
        catch
        {
            try
            {
                DcApiToken = BotConfig.GetConfig()["MainConfig"]["Discord_API_Token"];
            }
            catch
            {
                SentrySdk.CaptureMessage("Discord API Token could not be loaded.");
                logger.Fatal(
                    "Der Discord API Token konnte nicht geladen werden.");
                logger.Fatal("Drücke eine beliebige Taste um das Programm zu beenden.");
                throw new ApplicationException();
            }
        }

        var client = new KawaiiClient();


        builder.Services.AddRazorPages();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddServerSideBlazor()
            .AddHubOptions(options => { options.MaximumReceiveMessageSize = 32 * 1024 * 100; });
        builder.Services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog());
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddBlazorBootstrap();
        builder.Services.AddSingleton<UserService>();
        builder.Services.AddBlazorise(options => { options.Immediate = true; }).AddBootstrapProviders()
            .AddBootstrap5Providers().AddBootstrap5Components().AddBootstrapComponents();
        builder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });

        builder.Services.AddAuthentication(opt =>
            {
                opt.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                opt.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                opt.DefaultChallengeScheme = DiscordDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.LogoutPath = "/logout";
            })
            .AddDiscord(x =>
            {
                x.AppId = BotConfig.GetConfig()["WebUI"]["ClientID"];
                x.AppSecret = BotConfig.GetConfig()["WebUI"]["ClientSecret"];
                x.Scope.Add("guilds");
                x.AccessDeniedPath = "/OAuthError";
                x.SaveTokens = true;
                x.Prompt = DiscordOptions.PromptTypes.None;
                x.ClaimActions.MapCustomJson(ClaimTypes.NameIdentifier,
                    element => { return AuthUtils.RetrieveId(element).Result; });
                x.ClaimActions.MapCustomJson(ClaimTypes.Role,
                    element => { return AuthUtils.RetrieveRole(element).Result; });
                x.ClaimActions.MapCustomJson("FullQualifiedDiscordName",
                    element => { return AuthUtils.RetrieveName(element).Result; });
            });

        ILoggerFactory loggerFactory = null;
        if (loglevel == LogEventLevel.Debug)
        {
            loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(logger));
        }

        var dataSourceBuilder =
            new NpgsqlDataSourceBuilder(DatabaseService.GetConnectionString()).UseLoggerFactory(loggerFactory);
        var dataSource = dataSourceBuilder.Build();

        builder.Services
            .AddLogging(lb => lb.AddSerilog())
            .AddSingleton(client)
            .AddSingleton(dataSource)
            .AddHostedService<DiscordBotService>()
            .AddHealthChecks()
                .AddCheck<BotReadinessHealthCheck>("ready");

        var tempProvider = builder.Services.BuildServiceProvider();
        CurrentApplication.ServiceProvider = tempProvider;

        logger.Information("Connecting to Database...");
        var spinner = new ConsoleSpinner();
        spinner.Start();
        spinner.Stop();
        logger.Information("Database connected!");
        await DatabaseService.InitializeAndUpdateDatabaseTables();

        var app = builder.Build();

        // Keep global service provider reference
        CurrentApplication.ServiceProvider = app.Services;

        // Map Cloud Native health checks
        app.MapHealthChecks("/health/live"); // liveness (process up)
        app.MapHealthChecks("/health/ready"); 

        _ = RunAspAsync(app);
        await Task.Delay(-1);
    }

    // Discord bot runtime and command/event handlers are hosted in DiscordBotService (IHostedService).



    private static async Task RunAspAsync(WebApplication app)
    {
        bool enabled;
        int port;

        try
        {
            enabled = bool.Parse(BotConfig.GetConfig()["WebUI"]["Active"]);
        }
        catch
        {
            enabled = false;
        }

        if (!enabled)
        {
            CurrentApplication.Logger.Information("WebUI is disabled.");
            return;
        }

        port = 8085;


        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        // bind to localhost to use a reverse proxy like nginx, apache or iis
        app.Urls.Add($"http://localhost:{port}");


        bool useHttps;
        try
        {
            useHttps = bool.Parse(BotConfig.GetConfig()["WebUI"]["UseHttps"]);
        }
        catch
        {
            useHttps = false;
        }

        string dashboardUrl;
        try
        {
            dashboardUrl = BotConfig.GetConfig()["WebUI"]["DashboardURL"];
        }
        catch
        {
            dashboardUrl = "localhost";
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.Use((ctx, next) =>
        {
            ctx.Request.Host = new HostString(dashboardUrl);
            ctx.Request.Scheme = useHttps ? "https" : "http";
            return next();
        });


        app.UseAuthentication();
        app.UseAuthorization();

        app.UseCookiePolicy(new CookiePolicyOptions
        {
            MinimumSameSitePolicy = SameSiteMode.Lax
        });
        app.UseMiddleware<RoleRefreshMiddleware>();
        app.MapBlazorHub();
        app.MapDefaultControllerRoute();
        app.MapFallbackToPage("/_Host");

        CurrentApplication.Logger.Information("Starting WebUI on port " + port + "...");
        TempVariables.WebUiApp = app;
        await app.StartAsync();
        TempVariables.IsWebUiRunning = true;
        CurrentApplication.Logger.Information("WebUI started!");
    }


    public static class TempVariables
    {
        public static bool IsWebUiRunning { get; set; }
        public static WebApplication WebUiApp { get; set; }
    }
}

public static class GlobalProperties
{
    // Server Staffrole ID
    public static ulong StaffRoleId { get; } = ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["StaffRoleId"]);

    // Debug Mode
    public static bool DebugMode { get; } = ParseBoolean(BotConfig.GetConfig()["MainConfig"]["DebugMode"]);

    // Bot Owner ID
    public static ulong BotOwnerId { get; } = ulong.Parse(BotConfig.GetConfig()["MainConfig"]["BotOwnerId"]);

    public static DiscordGuild AGCGuild { get; set; }

    public static ulong ErrorTrackingChannelId { get; } =
        ulong.Parse(BotConfig.GetConfig()["MainConfig"]["ErrorTrackingChannelId"]);

    private static bool ParseBoolean(string boolString)
    {
        if (bool.TryParse(boolString, out bool parsedBool))
            return parsedBool;
        return false;
    }
}
