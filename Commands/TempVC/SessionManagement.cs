﻿#region

using AGC_Management.Attributes;
using AGC_Management.Services;
using AGC_Management.TempVoice;
using AGC_Management.Utils;
using DisCatSharp.Exceptions;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands.TempVC;

[Group("session")]
public sealed class SessionManagement : TempVoiceHelper
{
    [Command("save")]
    [Aliases("safe")]
    [RequireDatabase]
    public async Task SessionSave(CommandContext ctx)
    {
        _ = Task.Run(async () =>
        {
            List<ulong> blockedusers = new();
            List<ulong> permittedusers = new();
            List<long> dbChannels = await GetChannelIDFromDB(ctx);
            bool hidden = false;
            bool locked = false;
            List<ulong> channelmods = new();
            DiscordChannel userChannel = ctx.Member?.VoiceState?.Channel;

            if (userChannel == null || !dbChannels.Contains((long)userChannel?.Id))
            {
                await NoChannel(ctx);
                return;
            }

            if (userChannel != null && dbChannels.Contains((long)userChannel.Id))
            {
                var msg = await ctx.RespondAsync(
                    "<a:loading_agc:1084157150747697203> **Lade...** Versuche Channel zu speichern...");
                List<string> Query = new()
                {
                    "userid"
                };
                Dictionary<string, object> WhereCondiditons = new()
                {
                    { "userid", (long)ctx.User.Id }
                };
                bool hasSession = false;
                var usersession =
                    await DatabaseService.SelectDataFromTable("tempvoicesession", Query, WhereCondiditons);
                foreach (var user in usersession)
                {
                    hasSession = true;
                }


                if (hasSession)
                {
                    // if session is there delete it
                    Dictionary<string, (object value, string comparisonOperator)> whereConditions = new()
                    {
                        { "userid", ((long)ctx.User.Id, "=") }
                    };

                    await DatabaseService.DeleteDataFromTable("tempvoicesession", whereConditions);
                }

                channelmods = await RetrieveChannelMods(userChannel);

                var overwrite =
                    userChannel.PermissionOverwrites.FirstOrDefault(o => o.Id == ctx?.Guild?.EveryoneRole.Id);
                if (overwrite?.CheckPermission(Permissions.UseVoice) == PermissionLevel.Denied)
                {
                    locked = true;
                }

                if (overwrite == null || overwrite?.CheckPermission(Permissions.UseVoice) == PermissionLevel.Unset)
                {
                    locked = false;
                }

                if (overwrite?.CheckPermission(Permissions.AccessChannels) == PermissionLevel.Denied)
                {
                    hidden = true;
                }

                if (overwrite == null ||
                    overwrite?.CheckPermission(Permissions.AccessChannels) == PermissionLevel.Unset)
                {
                    hidden = false;
                }


                string blocklist = string.Empty;
                string permitlist = string.Empty;
                var buserow = userChannel.PermissionOverwrites
                    .Where(x => x.Type == OverwriteType.Member)
                    .Where(x => x.CheckPermission(Permissions.UseVoice) == PermissionLevel.Denied).Select(x => x.Id)
                    .ToList();

                var puserow = userChannel.PermissionOverwrites
                    .Where(x => x.CheckPermission(Permissions.UseVoice) == PermissionLevel.Allowed)
                    .Where(x => x.Type == OverwriteType.Member)
                    .Where(x => x.Id != ctx.User.Id)
                    .Select(x => x.Id)
                    .ToList();
                foreach (var user in buserow)
                {
                    blockedusers.Add(user);
                }

                foreach (var user in puserow)
                {
                    permittedusers.Add(user);
                }

                blocklist = string.Join(", ", blockedusers);
                permitlist = string.Join(", ", permittedusers);
                var channelmodsdata = string.Join(", ", channelmods);
                Dictionary<string, object> data = new()
                {
                    { "userid", (long)ctx.User.Id },
                    { "channelname", userChannel.Name },
                    { "channelbitrate", userChannel.Bitrate },
                    { "channellimit", userChannel.UserLimit },
                    { "blockedusers", blocklist },
                    { "permitedusers", permitlist },
                    { "locked", locked },
                    { "hidden", hidden },
                    { "sessionskip", false },
                    { "channelmods", channelmodsdata }
                };
                await DatabaseService.InsertDataIntoTable("tempvoicesession", data);
                await msg.ModifyAsync(
                    "<:success:1085333481820790944> **Erfolg!** Die Kanaleinstellungen wurden erfolgreich **gespeichert**!");
            }
        });
    }


    [Command("delete")]
    [RequireDatabase]
    public async Task SessionReset(CommandContext ctx)
    {
        _ = Task.Run(async () =>
        {
            var msg = await ctx.RespondAsync(
                "<a:loading_agc:1084157150747697203> **Lade...** Versuche Kanaleinstellungen zu löschen...");
            List<string> Query = new()
            {
                "userid"
            };
            Dictionary<string, object> WhereCondiditons = new()
            {
                { "userid", (long)ctx.User.Id }
            };
            bool hasSession = false;
            var usersession =
                await DatabaseService.SelectDataFromTable("tempvoicesession", Query, WhereCondiditons);
            foreach (var user in usersession)
            {
                hasSession = true;
            }

            Dictionary<string, (object value, string comparisonOperator)> whereConditions = new()
            {
                { "userid", ((long)ctx.User.Id, "=") }
            };

            int rowsDeleted =
                await DatabaseService.DeleteDataFromTable("tempvoicesession", whereConditions);

            if (!hasSession)
            {
                await msg.ModifyAsync(
                    "\u274c Du hast keine gespeicherte Sitzung.");
                return;
            }


            await msg.ModifyAsync(
                "<:success:1085333481820790944> **Erfolg!** Die Kanaleinstellungen wurden erfolgreich **gelöscht**!");
        });
    }


    [Command("skip")]
    [RequireDatabase]
    public async Task SessionSkip(CommandContext ctx)
    {
        _ = Task.Run(async () =>
        {
            var msg = await ctx.RespondAsync(
                "<a:loading_agc:1084157150747697203> **Lade...** Versuche Sessionskip zu bearbeiten...");
            List<string> Query = new()
            {
                "userid"
            };

            Dictionary<string, object> WhereCondiditons_ = new()
            {
                { "userid", (long)ctx.User.Id }
            };
            bool hasSession = false;
            var usersession =
                await DatabaseService.SelectDataFromTable("tempvoicesession", Query, WhereCondiditons_);
            foreach (var user in usersession)
            {
                hasSession = true;
            }

            if (!hasSession)
            {
                await msg.ModifyAsync(
                    "\u274c Du hast keine gespeicherte Sitzung.");
                return;
            }

            List<string> dataQuery = new()
            {
                "*"
            };

            Dictionary<string, object> WhereCondiditons = new()
            {
                { "userid", (long)ctx.User.Id }
            };

            var session =
                await DatabaseService.SelectDataFromTable("tempvoicesession", dataQuery, WhereCondiditons);

            var sessionSkip = false;
            foreach (var user in session)
            {
                if (user["userid"].ToString() == ctx.User.Id.ToString())
                {
                    sessionSkip = (bool)user["sessionskip"];
                }
            }

            bool newSessionSkip = !sessionSkip;

            string named = newSessionSkip ? "aktiv" : "inaktiv";

            var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

            await using var cmd =
                con.CreateCommand("UPDATE tempvoicesession SET sessionskip = @sessionskip WHERE userid = @userid");
            cmd.Parameters.AddWithValue("sessionskip", newSessionSkip);
            cmd.Parameters.AddWithValue("userid", (long)ctx.User.Id);
            await cmd.ExecuteNonQueryAsync();
            var successstring =
                $"<:success:1085333481820790944> **Erfolg!** Sessionskip wurde erfolgreich auf ``{named}`` **geändert**!";
            if (newSessionSkip)
            {
                successstring +=
                    "\n\n**Hinweis:** Sessionskip wird automatisch deaktiviert, wenn du dir einen Channel erstellst!";
            }

            await msg.ModifyAsync(successstring
            );
        });
    }

    [Command("read")]
    [Aliases("show", "info")]
    [RequireDatabase]
    public async Task SessionRead(CommandContext ctx)
    {
        _ = Task.Run(async () =>
        {
            var msg = await ctx.RespondAsync(
                "<a:loading_agc:1084157150747697203> **Lade...** Versuche Kanaleinstellungen zu lesen...");
            List<string> Query = new()
            {
                "userid"
            };

            Dictionary<string, object> WhereCondiditons_ = new()
            {
                { "userid", (long)ctx.User.Id }
            };
            bool hasSession = false;
            var usersession =
                await DatabaseService.SelectDataFromTable("tempvoicesession", Query, WhereCondiditons_);
            foreach (var user in usersession)
            {
                hasSession = true;
            }

            if (!hasSession)
            {
                await msg.ModifyAsync(
                    "\u274c Du hast keine gespeicherte Sitzung.");
                return;
            }

            List<string> dataQuery = new()
            {
                "*"
            };

            Dictionary<string, object> WhereCondiditons = new()
            {
                { "userid", (long)ctx.User.Id }
            };

            var session =
                await DatabaseService.SelectDataFromTable("tempvoicesession", dataQuery, WhereCondiditons);
            foreach (var user in session)
            {
                if (user["userid"].ToString() == ctx.User.Id.ToString())
                {
                    string channelname = user["channelname"].ToString();
                    string channelbitrate = user["channelbitrate"].ToString();
                    string channellimit = user["channellimit"].ToString();

                    if (channellimit == "0")
                    {
                        channellimit = "Kein Limit";
                    }

                    var caseid = ToolSet.GenerateCaseID();
                    string blockedusers = user["blockedusers"].ToString();
                    string permitedusers = user["permitedusers"].ToString();
                    string locked = user["locked"].ToString();
                    string hidden = user["hidden"].ToString();
                    bool sessionskip = (bool)user["sessionskip"];
                    string pu = string.IsNullOrEmpty(permitedusers) ? "Keine" : permitedusers;
                    string bu = string.IsNullOrEmpty(blockedusers) ? "Keine" : blockedusers;
                    string cm = string.IsNullOrEmpty(user["channelmods"].ToString())
                        ? "Keine"
                        : user["channelmods"].ToString();

                    DiscordEmbedBuilder ebb = new()
                    {
                        Title =
                            $"{BotConfig.GetConfig()["ServerConfig"]["ServerNameInitials"]} TempVC Kanaleinstellungen",
                        Description = $"**Kanalname:** {channelname}\n" +
                                      $"**Kanalbitrate:** {channelbitrate} kbps\n" +
                                      $"**Kanallimit:** {channellimit}\n\n" +
                                      $"**Gesperrte Benutzer:** ```{bu}```\n" +
                                      $"**Zugelassene Benutzer:** ```{pu}```\n" +
                                      $"**Kanalmods:** ```{cm}```\n\n" +
                                      $"**Sessionskip aktiv:** {sessionskip}\n" +
                                      $"**Gesperrt:** {locked}\n" +
                                      $"**Versteckt:** {hidden}\n",
                        Color = BotConfig.GetEmbedColor()
                    };
                    var mb = new DiscordMessageBuilder();
                    List<DiscordButtonComponent> buttons = new(2)
                    {
                        new DiscordButtonComponent(ButtonStyle.Secondary, $"close_msg_{caseid}",
                            "Nachricht schließen")
                    };
                    mb.WithEmbed(ebb);
                    mb.AddComponents(buttons);
                    var nmb = new DiscordMessageBuilder();
                    nmb.WithEmbed(ebb);
                    msg = await msg.ModifyAsync(mb);
                    var interactiviy = ctx.Client.GetInteractivity();
                    var response = await interactiviy.WaitForButtonAsync(msg, ctx.User,
                        TimeSpan.FromMinutes(5));
                    if (response.TimedOut)
                    {
                        await msg.ModifyAsync(nmb);
                    }

                    if (response.Result.Id == $"close_msg_{caseid}")
                    {
                        await msg.DeleteAsync();
                        try
                        {
                            await ctx.Message.DeleteAsync();
                        }
                        catch (NotFoundException)
                        {
                            // ignored
                        }
                    }
                }
            }
        });
    }
}