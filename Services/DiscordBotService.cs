using System.Reflection;
using DisCatSharp;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.EventArgs;
using DisCatSharp.ApplicationCommands.Exceptions;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.CommandsNext;
using DisCatSharp.CommandsNext.Exceptions;
using DisCatSharp.Entities;
using DisCatSharp.EventArgs;
using DisCatSharp.Interactivity;
using DisCatSharp.Interactivity.Extensions;
using KawaiiAPI.NET;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using AGC_Management.Utils;
using AGC_Management.Tasks;
using AGC_Management.Eventlistener;
using AGC_Management.Services;

namespace AGC_Management.Services;

public class DiscordBotService : IHostedService
{
    private readonly ILogger<DiscordBotService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly KawaiiClient _kawaiiClient;

    public static DiscordClient? Client { get; private set; }
    public static bool IsReady { get; private set; }

    public DiscordBotService(ILogger<DiscordBotService> logger, IServiceProvider serviceProvider, KawaiiClient kawaiiClient)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _kawaiiClient = kawaiiClient;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var logger = CurrentApplication.Logger;

        var dcApiToken = GetDiscordToken();

        logger.Information("Initializing Discord client (HostedService)...");

        var discord = new DiscordClient(new DiscordConfiguration
        {
            Token = dcApiToken,
            TokenType = TokenType.Bot,
            AutoReconnect = true,
            MinimumLogLevel = Microsoft.Extensions.Logging.LogLevel.Debug,
            Intents = DiscordIntents.All,
            LogTimestampFormat = "MMM dd yyyy - HH:mm:ss tt",
            DeveloperUserId = GlobalProperties.BotOwnerId,
            Locale = "de",
            ServiceProvider = _serviceProvider,
            MessageCacheSize = 10000,
            ShowReleaseNotesInUpdateCheck = false,
            HttpTimeout = TimeSpan.FromSeconds(40)
        });

        discord.MessageCreated += async (s, e) => await new TempVCMessageLogger().MessageCreated(s, e);

        try
        {
            string bprefix = BotConfig.GetConfig()["MainConfig"]["BotPrefix"] ?? "!!!";
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
        commands.CommandErrored += Commands_CommandErrored;

        discord.UseInteractivity(new InteractivityConfiguration
        {
            Timeout = TimeSpan.FromMinutes(2),
        });

        commands.RegisterCommands(Assembly.GetExecutingAssembly());

        var appCommands = discord.UseApplicationCommands(new ApplicationCommandsConfiguration
        {
            ServiceProvider = _serviceProvider, DebugStartup = true, EnableDefaultHelp = false
        });
        appCommands.SlashCommandExecuted += LogCommandExecution;
        appCommands.SlashCommandErrored += Discord_SlashCommandErrored;
        appCommands.RegisterGlobalCommands(Assembly.GetExecutingAssembly());

        await discord.ConnectAsync();
        await Task.Delay(5000, cancellationToken);

        CurrentApplication.DiscordClient = discord;
        Client = discord;

        _ = StartTasks(discord);
        _ = UpdateGuild(discord);

        CurrentApplication.TargetGuild =
            await discord.GetGuildAsync(ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"]));

        IsReady = true;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        IsReady = false;
        if (Client != null)
        {
            await Client.DisconnectAsync();
        }
    }

    private static string GetDiscordToken()
    {
        try
        {
            var debug = bool.Parse(BotConfig.GetConfig()["MainConfig"]["DebugMode"]);
            return debug
                ? BotConfig.GetConfig()["MainConfig"]["Discord_API_Token_DEB"]
                : BotConfig.GetConfig()["MainConfig"]["Discord_API_Token"];
        }
        catch
        {
            try
            {
                return BotConfig.GetConfig()["MainConfig"]["Discord_API_Token"];
            }
            catch
            {
                Sentry.SentrySdk.CaptureMessage("Discord API Token could not be loaded.");
                CurrentApplication.Logger.Fatal("Der Discord API Token konnte nicht geladen werden.");
                throw new ApplicationException();
            }
        }
    }

    private static Task Client_ComponentInteractionCreatedAsync(DiscordClient sender, ComponentInteractionCreateEventArgs e)
    {
        if (e.Id == "pgb-skip-left" || e.Id == "pgb-skip-right" || e.Id == "pgb-right" || e.Id == "pgb-left" || e.Id == "pgb-stop" || e.Id == "leftskip" || e.Id == "rightskip" || e.Id == "stop" || e.Id == "left" || e.Id == "right")
        {
            return e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
        }
        return Task.CompletedTask;
    }

    private static Task StartTasks(DiscordClient discord)
    {
        ModerationSystemTasks MST = new();
        _ = MST.StartRemovingWarnsPeriodically(discord);

        TempVoiceTasks TVT = new();
        _ = TVT.StartRemoveEmptyTempVoices(discord);

        _ = StatusUpdateTask(discord);
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

                    int tempvcCount = 0;
                    var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

                    string query = "SELECT channelid FROM tempvoice";
                    await using var cmd = con.CreateCommand(query);
                    await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
                    while (reader.Read())
                    {
                        ulong channelid = (ulong)reader.GetInt64(0);
                        var channel = await discord.TryGetChannelAsync(channelid);
                        if (channel != null)
                            tempvcCount++;
                    }

                    await discord.UpdateStatusAsync(new DiscordActivity($" Offene Temp-VCs: {tempvcCount}",
                        ActivityType.Custom));
                    await Task.Delay(TimeSpan.FromSeconds(30));

                    var guild = await discord.GetGuildAsync(
                        ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"]));
                    await discord.UpdateStatusAsync(new DiscordActivity($"Servermitglieder: {guild.MemberCount}",
                        ActivityType.Custom));
                    await Task.Delay(TimeSpan.FromSeconds(30));

                    int vcUsers = 0;
                    foreach (var channel in guild.Channels.Values)
                    {
                        if (channel.Type == ChannelType.Voice)
                            vcUsers += channel.Users.Count;
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
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        string query = "SELECT COUNT(*) FROM ticketstore where closed = False";
        await using NpgsqlCommand cmd = con.CreateCommand(query);
        int openTickets = Convert.ToInt32(cmd.ExecuteScalar());

        string query1 = "SELECT COUNT(*) FROM ticketstore where closed = True";
        await using NpgsqlCommand cmd1 = con.CreateCommand(query1);
        int closedTickets = Convert.ToInt32(cmd1.ExecuteScalar());
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
                    prefix = "!!!";
                }

            int commandStart = message.GetStringPrefixLength(prefix);
            return commandStart;
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
}
