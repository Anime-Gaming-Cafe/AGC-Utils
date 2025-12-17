#region

using System.Reflection;
using System.Security.Claims;
using AGC_Management.Controller;
using AGC_Management.Eventlistener;
using AGC_Management.Services;
using AGC_Management.Tasks;
using AGC_Management.Utils;
using BlazorBootstrap;
using Blazorise;
using Blazorise.Bootstrap;
using Blazorise.Bootstrap5;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.EventArgs;
using DisCatSharp.ApplicationCommands.Exceptions;
using DisCatSharp.CommandsNext.Exceptions;
using DisCatSharp.Interactivity;
using DisCatSharp.Interactivity.Extensions;
using Discord.OAuth2;
using KawaiiAPI.NET;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Win32.SafeHandles;
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
                if (version.StartsWith('v'))
                {
                    return version;
                }
                try
                {
                    Logger?.Warning($"Version string '{version}' doesn't follow the expected format (should start with 'v')");
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
                    Logger?.Error(ex, "Failed to generate timestamp for version string");
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
                Logger?.Error(ex, "Failed to determine version string");
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

        var serviceProvider = new ServiceCollection()
            .AddLogging(lb => lb.AddSerilog())
            .AddSingleton(client)
            .AddSingleton(dataSource)
            .BuildServiceProvider();
        CurrentApplication.ServiceProvider = serviceProvider;
        logger.Information("Connecting to Database...");
        var spinner = new ConsoleSpinner();
        spinner.Start();

        spinner.Stop();
        logger.Information("Database connected!");
        await DatabaseService.InitializeAndUpdateDatabaseTables();
        var discord = new DiscordClient(new DiscordConfiguration
        {
            Token = DcApiToken,
            TokenType = TokenType.Bot,
            AutoReconnect = true,
            MinimumLogLevel = LogLevel.Debug,
            Intents = DiscordIntents.All,
            LogTimestampFormat = "MMM dd yyyy - HH:mm:ss tt",
            DeveloperUserId = GlobalProperties.BotOwnerId,
            Locale = "de",
            ServiceProvider = serviceProvider,
            MessageCacheSize = 10000,
            ShowReleaseNotesInUpdateCheck = false,
            HttpTimeout = TimeSpan.FromSeconds(40)
        });

        discord.MessageCreated += async (s, e) => await new TempVCMessageLogger().MessageCreated(s, e);

        try
        {
            string bprefix = "!!!";
            bprefix = BotConfig.GetConfig()["MainConfig"]["BotPrefix"];
            CurrentApplication.BotPrefix = bprefix;
        }
        catch
        {
            CurrentApplication.BotPrefix = "!!!";
        }

        discord.RegisterEventHandlers(Assembly.GetExecutingAssembly());
        var commands = discord.UseCommandsNext(new CommandsNextConfiguration
        {
            PrefixResolver = GetPrefix,
            EnableDms = false,
            EnableMentionPrefix = true,
            IgnoreExtraArguments = true,
            EnableDefaultHelp = bool.Parse(BotConfig.GetConfig()["MainConfig"]["EnableBuiltInHelp"] ?? "false")
        });
        discord.ClientErrored += Discord_ClientErrored;
        discord.ComponentInteractionCreated += Client_ComponentInteractionCreatedAsync;
        commands.CommandExecuted += LogCommandExecution;

        discord.UseInteractivity(new InteractivityConfiguration
        {
            Timeout = TimeSpan.FromMinutes(2),
        });
        commands.RegisterCommands(Assembly.GetExecutingAssembly());
        var appCommands = discord.UseApplicationCommands(new ApplicationCommandsConfiguration
        {
            ServiceProvider = serviceProvider, DebugStartup = true, EnableDefaultHelp = false
        });
        appCommands.SlashCommandExecuted += LogCommandExecution;
        appCommands.SlashCommandErrored += Discord_SlashCommandErrored;
        appCommands.RegisterGlobalCommands(Assembly.GetExecutingAssembly());

        commands.CommandErrored += Commands_CommandErrored;

        await discord.ConnectAsync();
        await Task.Delay(5000);

        CurrentApplication.DiscordClient = discord;

        await StartTasks(discord);
        CurrentApplication.TargetGuild =
            await discord.GetGuildAsync(ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"]));
        _ = RunAspAsync(builder.Build());
        await Task.Delay(-1);
    }

    private static async Task Client_ComponentInteractionCreatedAsync(DiscordClient sender, ComponentInteractionCreateEventArgs e)
    {
        if (e.Id == "pgb-skip-left" || e.Id == "pgb-skip-right" || e.Id == "pgb-right" || e.Id == "pgb-left" || e.Id == "pgb-stop" || e.Id == "leftskip" || e.Id == "rightskip" || e.Id == "stop" || e.Id == "left" || e.Id == "right")
        {
            try
            {
                await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            }
            catch (Exception ex)
            {
                CurrentApplication.Logger.Error(ex, "Error while deferring");
            }
        }
    }


    private static Task StartTasks(DiscordClient discord)
    {
        //// start Warn Expire Task
        ModerationSystemTasks MST = new();
        _ = MST.StartRemovingWarnsPeriodically(discord);

        //// start TempVC Check Task
        TempVoiceTasks TVT = new();
        _ = TVT.StartRemoveEmptyTempVoices(discord);

        _ = StatusUpdateTask(discord);
        _ = UpdateGuild(discord);
        _ = ExtendedModerationSystemLoop.LaunchLoops();
        _ = RecalculateRanks.LaunchLoops();
        _ = CheckVCLevellingTask.Run();
        _ = GetVoiceMetrics.LaunchLoops();
        _ = LevelUtils.RunLeaderboardUpdate();
        _ = TicketSearchTools.LoadTicketsIntoCache();

        return Task.CompletedTask;
    }

    private static Task StatusUpdateTask(DiscordClient discord)
    {
        return Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await discord.UpdateStatusAsync(new DiscordActivity(
                        $"Version: {CurrentApplication.VersionString}",
                        ActivityType.Custom));
                    await Task.Delay(TimeSpan.FromSeconds(30));

                    await discord.UpdateStatusAsync(new DiscordActivity(await TicketString(), ActivityType.Custom));
                    await Task.Delay(TimeSpan.FromSeconds(30));

                    // get tempvc count
                    int tempvcCount = 0;
                    var constring = DatabaseService.GetConnectionString();
                    var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

                    string query = "SELECT channelid FROM tempvoice";
                    await using var cmd = con.CreateCommand(query);
                    await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
                    // get channels and fetch if they exist
                    while (reader.Read())
                    {
                        ulong channelid = (ulong)reader.GetInt64(0);
                        var channel = await discord.TryGetChannelAsync(channelid);
                        if (channel != null)
                        {
                            tempvcCount++;
                        }
                    }

                    await discord.UpdateStatusAsync(new DiscordActivity($" Offene Temp-VCs: {tempvcCount}",
                        ActivityType.Custom));
                    await Task.Delay(TimeSpan.FromSeconds(30));

                    // get membercount of agc
                    var guild = await discord.GetGuildAsync(
                        ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"]));
                    await discord.UpdateStatusAsync(new DiscordActivity($"Servermitglieder: {guild.MemberCount}",
                        ActivityType.Custom));
                    await Task.Delay(TimeSpan.FromSeconds(30));

                    // get vc user
                    int vcUsers = 0;
                    // for each channel in agc
                    foreach (var channel in guild.Channels.Values)
                    {
                        // if channel is voicechannel
                        if (channel.Type == ChannelType.Voice)
                        {
                            vcUsers += channel.Users.Count;
                        }
                    }

                    await discord.UpdateStatusAsync(new DiscordActivity($"User in VC: {vcUsers}", ActivityType.Custom));
                    await Task.Delay(TimeSpan.FromSeconds(30));
                }
                catch (Exception e)
                {
                    CurrentApplication.Logger.Error(e, "Error while updating status");
                }
            }
        });
    }


    private static async Task<string> TicketString()
    {
        int openTickets = 0;
        int closedTickets = 0;
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        string query = "SELECT COUNT(*) FROM ticketstore where closed = False";
        await using NpgsqlCommand cmd = con.CreateCommand(query);
        openTickets = Convert.ToInt32(cmd.ExecuteScalar());

        string query1 = "SELECT COUNT(*) FROM ticketstore where closed = True";
        await using NpgsqlCommand cmd1 = con.CreateCommand(query1);
        closedTickets = Convert.ToInt32(cmd1.ExecuteScalar());
        return $"Tickets: Offen: {openTickets} | Gesamt: {openTickets + closedTickets}";
    }


    private static async Task Discord_SlashCommandErrored(ApplicationCommandsExtension sender,
        SlashCommandErrorEventArgs e)
    {
		if (e.Exception is SlashExecutionChecksFailedException ex)
		{
			if (ex.FailedChecks.Any(x => x is ApplicationCommandRequireUserPermissionsAttribute))
			{
				var embed = EmbedGenerator.GetErrorEmbed(
					"You don't have the required permissions to execute this command.");
				await e.Context.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
					new DiscordInteractionResponseBuilder().AddEmbed(embed).AsEphemeral());
				e.Handled = true;
				return;
			}

			e.Handled = true;
		}
	}


    private static Task<int> GetPrefix(DiscordMessage message)
    {
        return Task.Run(() =>
        {
            string prefix;
            if (GlobalProperties.DebugMode)
                prefix = "!!!";
            else
                try
                {
                    prefix = BotConfig.GetConfig()["MainConfig"]["BotPrefix"];
                }
                catch
                {
                    prefix = "!!!"; //Fallback Config
                }

            int CommandStart = -1;
            CommandStart = message.GetStringPrefixLength(prefix);
            return CommandStart;
        });
    }

    private static async Task UpdateGuild(DiscordClient client)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        while (true)
        {
            GlobalProperties.AGCGuild =
                await client.GetGuildAsync(ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"]));
            await Task.Delay(TimeSpan.FromMinutes(5));
        }
    }

    private static async Task Discord_ClientErrored(DiscordClient sender, ClientErrorEventArgs e)
    {
        sender.Logger.LogError($"Exception occured: {e.Exception.GetType()}: {e.Exception.Message}");
        sender.Logger.LogError($"Stacktrace: {e.Exception.GetType()}: {e.Exception.StackTrace}");
        await ErrorReporting.SendErrorToDev(sender, sender.CurrentUser, e.Exception);
    }

    private static Task LogCommandExecution(CommandsNextExtension client, CommandExecutionEventArgs args)
    {
        _ = Task.Run(async () =>
        {
            var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            await using var com = con.CreateCommand(
                "INSERT INTO cmdexec (commandname, commandcontent, userid, timestamp) VALUES (@commandname, @commandcontent, @userid, @timestamp)");
            com.Parameters.AddWithValue("commandname", args.Command.Name);
            com.Parameters.AddWithValue("commandcontent", args.Context.Message.Content);
            com.Parameters.AddWithValue("userid", (long)args.Context.User.Id);
            com.Parameters.AddWithValue("timestamp", DateTimeOffset.Now.ToUnixTimeMilliseconds());
            await com.ExecuteNonQueryAsync();
        });
        return Task.CompletedTask;
    }

    private static Task LogCommandExecution(ApplicationCommandsExtension client, SlashCommandExecutedEventArgs args)
    {
        _ = Task.Run(async () =>
        {
            var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            await using var com = con.CreateCommand(
                "INSERT INTO cmdexec (commandname, commandcontent, userid, timestamp) VALUES (@commandname, @commandcontent, @userid, @timestamp)");
            com.Parameters.AddWithValue("commandname", args.Context);
            com.Parameters.AddWithValue("commandcontent", "NULL (Slash Command)");
            com.Parameters.AddWithValue("userid", (long)args.Context.User.Id);
            com.Parameters.AddWithValue("timestamp", DateTimeOffset.Now.ToUnixTimeMilliseconds());
            await com.ExecuteNonQueryAsync();
        });
        return Task.CompletedTask;
    }


    private static async Task Commands_CommandErrored(CommandsNextExtension cn, CommandErrorEventArgs e)
    {
        CurrentApplication.DiscordClient.Logger.LogError(e.Exception,
            $"Exception occured: {e.Exception.GetType()}: {e.Exception.Message}");
        if (e.Exception is ArgumentException)
        {
            if (e.Exception.Message.Contains("Description length cannot exceed 4096 characters."))
            {
                DiscordEmbedBuilder web;
                web = new DiscordEmbedBuilder
                {
                    Title = "Fehler | DescriptionTooLongException",

                    Color = new DiscordColor("#FF0000")
                };
                web.WithDescription($"Das Embed hat zu viele Zeichen.\n" +
                                    $"**Stelle sicher dass die Hauptsektion nicht mehr als 4096 Zeichen hat!**");
                web.WithFooter($"Fehler ausgelöst von {e.Context.User.UsernameWithDiscriminator}");
                await e.Context.RespondAsync(embed: web, content: e.Context.User.Mention);
                return;
            }

            DiscordEmbedBuilder eb;
            eb = new DiscordEmbedBuilder
            {
                Title = "Fehler | BadArgumentException",

                Color = new DiscordColor("#FF0000")
            };
            eb.WithDescription($"Fehlerhafte Argumente.\n" +
                               $"**Stelle sicher dass alle Argumente richtig angegeben sind!**");
            eb.WithFooter($"Fehler ausgelöst von {e.Context.User.UsernameWithDiscriminator}");
            await e.Context.RespondAsync(embed: eb, content: e.Context.User.Mention);
            return;
        }

        if (e.Exception is CommandNotFoundException)
        {
            e.Handled = true;
            return;
        }


        if (e.Exception.Message == "No matching subcommands were found, and this group is not executable.")
        {
            e.Handled = true;
            return;
        }

        await ErrorReporting.SendErrorToDev(CurrentApplication.DiscordClient, e.Context.User, e.Exception);

        var embed = new DiscordEmbedBuilder
        {
            Title = "Fehler | CommandErrored",
            Color = new DiscordColor("#FF0000")
        };
        embed.WithDescription($"Es ist ein Fehler aufgetreten.\n" +
                              $"**Fehler: {e.Exception.Message}**");
        embed.WithFooter($"Fehler ausgelöst von {e.Context.User.UsernameWithDiscriminator}");
        await e.Context.RespondAsync(embed: embed, content: e.Context.User.Mention);
    }

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

        try
        {
            port = int.Parse(BotConfig.GetConfig()["WebUI"]["Port"]);
        }
        catch
        {
            port = 5000; // fallback
        }


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
