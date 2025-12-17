#region

using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.Exceptions;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.TempVoice;

public class TempVoiceHelper : BaseCommandModule
{
    private static readonly Dictionary<ulong, string> levelroles = new()
    {
        { 750402390691152005, "5+" },
        { 798562254408777739, "10+" },
        { 750450170189185024, "15+" },
        { 798555933089071154, "20+" },
        { 750450342474416249, "25+" },
        { 750450621492101280, "30+" },
        { 798555135071617024, "35+" },
        { 751134108893184072, "40+" },
        { 776055585912389673, "45+" },
        { 750458479793274950, "50+" },
        { 798554730988306483, "60+" },
        { 757683142894157904, "70+" },
        { 810231454985486377, "80+" },
        { 810232899713630228, "90+" },
        { 810232892386705418, "100+" }
    };

    private static readonly Dictionary<ulong, string> debuglevelroles = new()
    {
        { 1116778938073616545, "001" },
        { 1116778981442723870, "002" }
    };


    protected static string GetVCConfig(string key)
    {
        return BotConfig.GetConfig()["TempVC"][$"{key}"];
    }

    protected static async Task RemoveChannelFromDB(ulong cid)
    {
        Dictionary<string, (object value, string comparisonOperator)>
            DeletewhereConditions = new()
            {
                { "channelid", ((long)cid, "=") }
            };
        await DatabaseService.DeleteDataFromTable("tempvoice", DeletewhereConditions);
    }

    protected static async Task<bool> NoChannel(CommandContext ctx)
    {
        var errorMessage = $"<:attention:1085333468688433232> **Fehler!** " +
                           $"Du besitzt keinen eigenen Kanal oder der Kanal gehört dir nicht. " +
                           $"Wenn du keinen Kanal hast, kannst du einen unter <#{GetVCConfig("Creation_Channel_ID")}> erstellen.";

        await ctx.RespondAsync(errorMessage);
        return true;
    }

    protected static async Task<bool> NoChannel(DiscordInteraction interaction)
    {
        var errorMessage = $"<:attention:1085333468688433232> **Fehler!** " +
                           $"Du besitzt keinen eigenen Kanal oder der Kanal gehört dir nicht. " +
                           $"Wenn du keinen Kanal hast, kannst du einen unter <#{GetVCConfig("Creation_Channel_ID")}> erstellen.";
        DiscordInteractionResponseBuilder ib = new()
        {
            IsEphemeral = true
        };
        await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            ib.WithContent(errorMessage));
        return true;
    }

    protected static string GetBetterUsername(DiscordMember member)
    {
        if (member.IsMigrated)
        {
            if ("" == member.Nickname) return $"{member.Nickname} ({member.Username})";

            return $"{member.DisplayName} ({member.Username})";
        }

        if ("" == member.Nickname) return $"{member.Nickname} ({member.UsernameWithDiscriminator})";

        return $"{member.UsernameWithDiscriminator}";
    }

    protected static string GetBetterUsernameWithID(DiscordMember member)
    {
        return $"{GetBetterUsername(member)} ``{member.Id}``";
    }

    protected static async Task<bool> IsChannelMod(DiscordChannel? channel, DiscordUser user)
    {
        if (channel == null) return false;

        List<string> Query = new()
        {
            "channelmods"
        };
        Dictionary<string, object> QueryConditions = new()
        {
            { "channelid", (long)channel.Id }
        };
        var QueryResult = await DatabaseService.SelectDataFromTable("tempvoice",
            Query, QueryConditions);
        var isMod = false;
        foreach (var result in QueryResult)
            if (result["channelmods"].ToString().Contains(user.Id.ToString()))
                isMod = true;

        return isMod;
    }

    protected static async Task<List<ulong>> RetrieveChannelMods(DiscordChannel channel)
    {
        List<string> Query = new()
        {
            "channelmods"
        };
        Dictionary<string, object> QueryConditions = new()
        {
            { "channelid", (long)channel.Id }
        };
        var QueryResult =
            await DatabaseService.SelectDataFromTable("tempvoice", Query, QueryConditions);

        List<ulong> channelMods = new();
        foreach (var result in QueryResult)
            try
            {
                var mods = result["channelmods"].ToString()
                    .Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var mod in mods)
                    if (ulong.TryParse(mod, out var parsedMod))
                        channelMods.Add(parsedMod);
            }
            catch (Exception)
            {
            }

        return channelMods;
    }

    protected static async Task<bool> ChannelHasMods(DiscordChannel channel)
    {
        var channelMods = await RetrieveChannelMods(channel);
        if (channelMods.Count > 0) return true;

        return false;
    }

    protected static async Task ResetChannelMods(DiscordChannel channel)
    {
        await using (NpgsqlConnection conn = new(DatabaseService.GetConnectionString()))
        {
            await conn.OpenAsync();
            var sql = "UPDATE tempvoice SET channelmods = @mods WHERE channelid = @channelid";
            await using (NpgsqlCommand command = new(sql, conn))
            {
                command.Parameters.AddWithValue("@mods", string.Empty);
                command.Parameters.AddWithValue("@channelid", (long)channel.Id);
                var affected = await command.ExecuteNonQueryAsync();
            }
        }
    }

    protected static async Task UpdateChannelMods(DiscordChannel channel, List<ulong> channelMods)
    {
        await using (NpgsqlConnection conn = new(DatabaseService.GetConnectionString()))
        {
            await conn.OpenAsync();
            var sql = "UPDATE tempvoice SET channelmods = @mods WHERE channelid = @channelid";
            await using (NpgsqlCommand command = new(sql, conn))
            {
                command.Parameters.AddWithValue("@mods", string.Join(", ", channelMods));
                command.Parameters.AddWithValue("@channelid", (long)channel.Id);
                var affected = await command.ExecuteNonQueryAsync();
            }
        }
    }


    private static async Task<List<long>> GetChannelIDFromDB(DiscordInteraction interaction)
    {
        List<long> dbChannels = new();

        List<string> Query = new()
        {
            "channelid"
        };
        Dictionary<string, object> QueryConditions = new()
        {
            { "ownerid", (long)interaction.User.Id }
        };
        var QueryResult =
            await DatabaseService.SelectDataFromTable("tempvoice", Query, QueryConditions);
        foreach (var result in QueryResult)
        {
            var chid = result["channelid"];
            var id = (long)chid;
            dbChannels.Add(id);
        }

        return dbChannels;
    }


    protected static async Task<List<long>> GetChannelIDFromDB(CommandContext ctx)
    {
        List<long> dbChannels = new();

        List<string> Query = new()
        {
            "channelid"
        };
        Dictionary<string, object> QueryConditions = new()
        {
            { "ownerid", (long)ctx.User.Id }
        };
        var QueryResult =
            await DatabaseService.SelectDataFromTable("tempvoice", Query, QueryConditions);
        foreach (var result in QueryResult)
        {
            var chid = result["channelid"];
            var id = (long)chid;
            dbChannels.Add(id);
        }

        return dbChannels;
    }

    protected static async Task<List<long>> GetAllChannelIDsFromDB()
    {
        List<long> dbChannels = new();

        List<string> Query = new()
        {
            "channelid"
        };
        var QueryResult =
            await DatabaseService.SelectDataFromTable("tempvoice", Query, null);
        foreach (var result in QueryResult)
        {
            var chid = result["channelid"];
            var id = (long)chid;
            dbChannels.Add(id);
        }

        return dbChannels;
    }

    protected static async Task<long?> GetChannelOwnerID(CommandContext ctx)
    {
        long? channelownerid = null;
        try
        {
            List<string> query = new()
            {
                "ownerid"
            };

            Dictionary<string, object> queryConditions = new()
            {
                { "channelid", (long)ctx.Member.VoiceState?.Channel.Id }
            };

            var queryResult =
                await DatabaseService.SelectDataFromTable("tempvoice", query, queryConditions);

            foreach (var result in queryResult)
                if (result.TryGetValue("ownerid", out var ownerIdValue) && ownerIdValue is long ownerId)
                {
                    channelownerid = ownerId;
                    break;
                }
        }
        catch
        {
            // Handle exception
        }

        return channelownerid;
    }

    protected static async Task<long?> GetChannelOwnerID(DiscordMember user)
    {
        long? channelownerid = null;
        try
        {
            List<string> query = new()
            {
                "ownerid"
            };

            Dictionary<string, object> queryConditions = new()
            {
                { "channelid", (long)user.VoiceState?.Channel.Id }
            };

            var queryResult =
                await DatabaseService.SelectDataFromTable("tempvoice", query, queryConditions);

            foreach (var result in queryResult)
                if (result.TryGetValue("ownerid", out var ownerIdValue) && ownerIdValue is long ownerId)
                {
                    channelownerid = ownerId;
                    break;
                }
        }
        catch
        {
            // Handle exception
        }

        return channelownerid;
    }


    protected static async Task<long?> GetChannelOwnerID(DiscordInteraction interaction)
    {
        long? channelownerid = null;
        try
        {
            List<string> query = new()
            {
                "ownerid"
            };
            var discordGuild = interaction.Guild;
            var discordMember = await discordGuild.GetMemberAsync(interaction.User.Id);
            Dictionary<string, object> queryConditions = new()
            {
                { "channelid", (long)discordMember.VoiceState?.Channel.Id }
            };

            var queryResult =
                await DatabaseService.SelectDataFromTable("tempvoice", query, queryConditions);

            foreach (var result in queryResult)
                if (result.TryGetValue("ownerid", out var ownerIdValue) && ownerIdValue is long ownerId)
                {
                    channelownerid = ownerId;
                    break;
                }
        }
        catch
        {
            // Handle exception
        }

        return channelownerid;
    }

    protected static async Task<long?> GetChannelOwnerID(DiscordChannel channel)
    {
        long? channelownerid = null;
        try
        {
            List<string> query = new()
            {
                "ownerid"
            };

            Dictionary<string, object> queryConditions = new()
            {
                { "channelid", (long)channel.Id }
            };

            var queryResult =
                await DatabaseService.SelectDataFromTable("tempvoice", query, queryConditions);

            foreach (var result in queryResult)
                if (result.TryGetValue("ownerid", out var ownerIdValue) && ownerIdValue is long ownerId)
                {
                    channelownerid = ownerId;
                    break;
                }
        }
        catch
        {
            // Handle exception
        }

        return channelownerid;
    }

    protected static bool SelfCheck(CommandContext ctx, DiscordMember member)
    {
        if (ctx.User.Id == member.Id) return true;

        return false;
    }

    protected static async Task<List<long>> GetAllTempChannels()
    {
        var list = new List<long>();
        List<string> query = new()
        {
            "channelid"
        };
        var result = await DatabaseService.SelectDataFromTable("tempvoice", query, null);
        foreach (var item in result)
        {
            var ChannelId = (long)item["channelid"];
            list.Add(ChannelId);
        }

        return list;
    }

    protected static async Task PanelLockChannel(DiscordInteraction interaction)
    {
        var dbChannels = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!dbChannels.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && dbChannels.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var default_role = interaction.Guild.EveryoneRole;
            var channel = member.VoiceState.Channel;
            var overwrite = channel.PermissionOverwrites.FirstOrDefault(o => o.Id == default_role.Id);
            if (overwrite?.CheckPermission(Permissions.UseVoice) == PermissionLevel.Denied)
            {
                DiscordInteractionResponseBuilder builder = new()
                {
                    IsEphemeral = true,
                    Content = "<:attention:1085333468688433232> Der Channel ist bereits gesperrt!"
                };
                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder);
                return;
            }

            var overwrites = userChannel.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();
            overwrites = overwrites.Merge(default_role, Permissions.None, Permissions.UseVoice);
            await userChannel.ModifyAsync(x => x.PermissionOverwrites = overwrites);

            DiscordInteractionResponseBuilder builder_ = new()
            {
                IsEphemeral = true,
                Content = $"<:success:1085333481820790944> <#{channel.Id}> ist nun **gesperrt**."
            };
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder_);
        }
    }

    protected static async Task PanelUnlockChannel(DiscordInteraction interaction)
    {
        var dbChannels = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!dbChannels.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && dbChannels.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var default_role = interaction.Guild.EveryoneRole;
            var channel = member.VoiceState.Channel;
            var overwrite = channel.PermissionOverwrites.FirstOrDefault(o => o.Id == default_role.Id);
            if (overwrite == null || overwrite?.CheckPermission(Permissions.UseVoice) == PermissionLevel.Unset)
            {
                DiscordInteractionResponseBuilder builder = new()
                {
                    IsEphemeral = true,
                    Content = "<:attention:1085333468688433232> Der Channel ist bereits entsperrt!"
                };
                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder);
                return;
            }

            var overwrites = userChannel.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();
            overwrites = overwrites.Merge(default_role, Permissions.None, Permissions.None, Permissions.UseVoice);
            await userChannel.ModifyAsync(x => x.PermissionOverwrites = overwrites);

            DiscordInteractionResponseBuilder builder_ = new()
            {
                IsEphemeral = true,
                Content = $"<:success:1085333481820790944> <#{channel.Id}> ist nun **entsperrt**."
            };
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder_);
        }
    }


    protected static async Task PanelHideChannel(DiscordInteraction interaction)
    {
        var dbChannels = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!dbChannels.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && dbChannels.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var default_role = interaction.Guild.EveryoneRole;
            var channel = member.VoiceState.Channel;
            var overwrite = channel.PermissionOverwrites.FirstOrDefault(o => o.Id == default_role.Id);
            if (overwrite?.CheckPermission(Permissions.AccessChannels) == PermissionLevel.Denied)
            {
                DiscordInteractionResponseBuilder builder = new()
                {
                    IsEphemeral = true,
                    Content = "<:attention:1085333468688433232> Der Channel ist bereits versteckt!"
                };
                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder);
                return;
            }

            var overwrites = userChannel.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();
            overwrites = overwrites.Merge(default_role, Permissions.None, Permissions.AccessChannels);
            await userChannel.ModifyAsync(x => x.PermissionOverwrites = overwrites);

            DiscordInteractionResponseBuilder builder_ = new()
            {
                IsEphemeral = true,
                Content = $"<:success:1085333481820790944> <#{channel.Id}> ist nun **versteckt**."
            };
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder_);
        }
    }

    protected static async Task PanelUnhideChannel(DiscordInteraction interaction)
    {
        var dbChannels = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!dbChannels.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && dbChannels.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var default_role = interaction.Guild.EveryoneRole;
            var channel = member.VoiceState.Channel;
            var overwrite = channel.PermissionOverwrites.FirstOrDefault(o => o.Id == default_role.Id);
            if (overwrite == null || overwrite?.CheckPermission(Permissions.AccessChannels) == PermissionLevel.Unset)
            {
                DiscordInteractionResponseBuilder builder = new()
                {
                    IsEphemeral = true,
                    Content = "<:attention:1085333468688433232> Der Channel ist bereits sichtbar!"
                };
                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder);
                return;
            }

            var overwrites = userChannel.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();
            overwrites = overwrites.Merge(default_role, Permissions.None, Permissions.None, Permissions.AccessChannels);
            await userChannel.ModifyAsync(x => x.PermissionOverwrites = overwrites);

            DiscordInteractionResponseBuilder builder_ = new()
            {
                IsEphemeral = true,
                Content = $"<:success:1085333481820790944> <#{channel.Id}> ist nun **sichtbar**."
            };
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder_);
        }
    }

    protected static async Task PanelChannelRename(DiscordInteraction interaction, DiscordClient client)
    {
        var current_timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
        var dbChannels = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!dbChannels.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && dbChannels.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var caseid = ToolSet.GenerateCaseID();
            var idstring = $"RenameModal-{caseid}";
            DiscordInteractionModalBuilder modal = new();
            modal.WithTitle("Channel Rename");
            modal.CustomId = idstring;
            modal.AddLabelComponent(new("Kanal umbennenen", component: new DiscordTextInputComponent(TextComponentStyle.Small, minLength: 1, maxLength: 100, placeholder: "Neuer Kanalname")));
            await interaction.CreateInteractionModalResponseAsync(modal);
            var interactivity = client.GetInteractivity();
            var result = await interactivity.WaitForModalAsync(idstring, TimeSpan.FromMinutes(1));
            if (result.TimedOut) return;

            var name = (result.Result.Interaction.Data.ModalComponents.OfType<DiscordLabelComponent>().First().Component as DiscordTextInputComponent)?.Value;
            await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            var channel = userChannel;
            long timestampdata = 0;
            List<string> Query = new()
            {
                "lastedited"
            };
            Dictionary<string, object> WhereCondiditons = new()
            {
                { "channelid", (long)channel.Id }
            };
            var dbtimestampdata = await DatabaseService.SelectDataFromTable("tempvoice", Query, WhereCondiditons);
            foreach (var data in dbtimestampdata) timestampdata = (long)data["lastedited"];

            var edit_timestamp = timestampdata;
            var math = current_timestamp - edit_timestamp;
            if (math < 300)
            {
                var calc = edit_timestamp + 300;
                var build = new DiscordFollowupMessageBuilder();
                build.WithContent(
                    $"<:attention:1085333468688433232> **Fehler!** Der Channel wurde in den letzten 5 Minuten schon einmal umbenannt. Bitte warte noch etwas, bevor du den Channel erneut umbenennen kannst. __Beachte:__ Auf diese Aktualisierung haben wir keinen Einfluss und dies Betrifft nur Bots. Erneut umbenennen kannst du den Channel <t:{calc}:R>.");
                build.IsEphemeral = true;
                await result.Result.Interaction.CreateFollowupMessageAsync(build);
                return;
            }

            var oldname = channel.Name;
            await channel.ModifyAsync(x => x.Name = name);
            await using (NpgsqlConnection conn = new(DatabaseService.GetConnectionString()))
            {
                await conn.OpenAsync();
                var sql = "UPDATE tempvoice SET lastedited = @timestamp WHERE channelid = @channelid";
                await using (NpgsqlCommand command = new(sql, conn))
                {
                    command.Parameters.AddWithValue("@timestamp", current_timestamp);
                    command.Parameters.AddWithValue("@channelid", (long)channel.Id);
                    await command.ExecuteNonQueryAsync();
                }
            }

            var builder = new DiscordFollowupMessageBuilder();
            builder.WithContent("<:success:1085333481820790944> **Erfolg!** Der Channel wurde erfolgreich umbenannt.");
            builder.IsEphemeral = true;
            await result.Result.Interaction.CreateFollowupMessageAsync(builder);
        }
    }

    protected static async Task PanelChannelLimit(DiscordInteraction interaction, DiscordClient client)
    {
        var dbChannels = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!dbChannels.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && dbChannels.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var caseid = ToolSet.GenerateCaseID();
            var idstring = $"LimitModal-{caseid}";
            DiscordInteractionModalBuilder modal = new();
            modal.WithTitle("Channel Limit");
            modal.CustomId = idstring;
            modal.AddLabelComponent(new("Kanal Limit festlegen", component: new DiscordTextInputComponent(TextComponentStyle.Small, minLength: 1, maxLength: 2, placeholder: "Limit zwischen 0 und 99 eingeben")));
            await interaction.CreateInteractionModalResponseAsync(modal);
            var interactivity = client.GetInteractivity();
            var result = await interactivity.WaitForModalAsync(idstring, TimeSpan.FromMinutes(1));
            if (result.TimedOut) return;

            var limit = (result.Result.Interaction.Data.ModalComponents.OfType<DiscordLabelComponent>().First().Component as DiscordTextInputComponent)?.Value;
            await result.Result.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            var climit = 0;
            try
            {
                climit = int.Parse(limit);
            }
            catch (FormatException)
            {
                var errbuilder = new DiscordFollowupMessageBuilder();
                errbuilder.WithContent(
                    "<:attention:1085333468688433232> **Fehler!** Stelle sicher, dass das Limit korrekt ist. Bitte gebe nur Zahlen von 0 - 99 ein.");
                errbuilder.IsEphemeral = true;
                await result.Result.Interaction.CreateFollowupMessageAsync(errbuilder);
                return;
            }

            var channel = userChannel;
            try
            {
                await channel.ModifyAsync(x => x.UserLimit = climit);
            }
            catch (BadRequestException)
            {
                var errbuilder = new DiscordFollowupMessageBuilder();
                errbuilder.WithContent(
                    "<:attention:1085333468688433232> **Fehler!** Stelle sicher, dass das Limit korrekt ist. Bitte gebe nur Zahlen von 0 - 99 ein.");
                errbuilder.IsEphemeral = true;
                await result.Result.Interaction.CreateFollowupMessageAsync(errbuilder);
                return;
            }

            var builder = new DiscordFollowupMessageBuilder();
            if (climit == 0)
            {
                builder.WithContent("<:success:1085333481820790944> **Erfolg!** Das Limit wurde erfolgreich entfernt.");
                builder.IsEphemeral = true;
                await result.Result.Interaction.CreateFollowupMessageAsync(builder);
                return;
            }

            builder.WithContent(
                $"<:success:1085333481820790944> **Erfolg!** Das Limit wurde erfolgreich auf {climit} gesetzt.");
            builder.IsEphemeral = true;
            await result.Result.Interaction.CreateFollowupMessageAsync(builder);
        }
    }

    protected static async Task PanelChannelInvite(DiscordInteraction interaction)
    {
        var db_channels = await GetAllChannelIDsFromDB();
        var my_db_channels = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        if (userChannel == null || !my_db_channels.Contains((long)userChannel?.Id))
        {
            await NoChannel(interaction);
            return;
        }

        if (!db_channels.Contains((long)userChannel?.Id))
        {
            var errorMessage =
                "<:attention:1085333468688433232> Du musst in einem TempVoice Kanal sein um Mitglieder einladen zu können!";
            DiscordInteractionResponseBuilder ib = new()
            {
                IsEphemeral = true
            };
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                ib.WithContent(errorMessage));
            return;
        }

        var options = new DiscordUserSelectComponent[]
        {
            new("Wähle den einzuladenden User aus.", "invite_selector", 1)
        };
        DiscordInteractionResponseBuilder builder_ = new()
        {
            IsEphemeral = true,
            Content = "Wähle die User aus, die du einladen möchtest."
        };
        var builder = builder_.AddComponents(options);
        await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, builder);
    }

    protected static async Task PanelChannelInviteCallback(DiscordInteraction interaction, DiscordClient sender)
    {
        var userid = interaction.Data.Values[0];
        var user = await interaction.Guild.GetMemberAsync(ulong.Parse(userid));
        var executor = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var send = false;
        if (user == interaction.User)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content = "<:attention:1085333468688433232> Du kannst dich nicht selbst einladen!"
                });
            return;
        }

        if (user.IsBot)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content = "<:attention:1085333468688433232> Du kannst keine Bots einladen!"
                });
            return;
        }

        if (user.VoiceState != null && user.VoiceState.Channel.Users.Contains(user))
        {
            await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content = $"<:attention:1085333468688433232> Der {user.Mention} ist bereits in deinem Channel!"
                });
            return;
        }

        try
        {
            var eb = new DiscordEmbedBuilder()
                .WithDescription(
                    $"Du wurdest von {interaction.User.Mention} eingeladen <#{interaction.Guild.GetMemberAsync(interaction.User.Id).Result.VoiceState?.Channel.Id}> beizutreten.")
                .WithColor(BotConfig.GetEmbedColor());
            await user.SendMessageAsync(eb);
            send = true;
        }
        catch (UnauthorizedException)
        {
            send = false;
        }

        if (send)
        {
            var role = interaction.Guild.EveryoneRole;
            var overwrites = executor.VoiceState?.Channel.PermissionOverwrites.Select(x => x.ConvertToBuilder())
                .ToList();
            overwrites.Merge(user, Permissions.AccessChannels | Permissions.UseVoice,
                Permissions.None);
            await executor.VoiceState?.Channel.ModifyAsync(x => { x.PermissionOverwrites = overwrites; });
            await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content =
                        $"<:success:1085333481820790944> {user.Mention} wurde in <#{interaction.Guild.GetMemberAsync(interaction.User.Id).Result.VoiceState.Channel.Id}> eingeladen."
                });
            return;
        }

        await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
            new DiscordInteractionResponseBuilder
            {
                IsEphemeral = true,
                Content =
                    $"<:attention:1085333468688433232> {user.Mention} konnte nicht eingeladen werden. Dieser User erlaubt keine DMs!"
            });
    }

    protected static async Task PanelChannelDelete(DiscordInteraction interaction, DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        if (userChannel == null || !db_channel.Contains((long)userChannel?.Id))
        {
            await NoChannel(interaction);
            return;
        }

        if (userChannel != null && db_channel.Contains((long)userChannel?.Id))
        {
            var caseid = ToolSet.GenerateCaseID();
            var channel = interaction.Guild.GetChannel(userChannel.Id);
            List<DiscordButtonComponent> buttons = new(2)
            {
                new DiscordButtonComponent(ButtonStyle.Success, $"chdel_accept_{caseid}", "Ja"),
                new DiscordButtonComponent(ButtonStyle.Danger, $"chdel_deny_{caseid}", "Nein")
            };
            var interactivity = client.GetInteractivity();
            var errorMessage =
                "<:attention:1085333468688433232> Möchtest du deinen Kanal wirklich löschen?";
            DiscordInteractionResponseBuilder ib = new()
            {
                IsEphemeral = true
            };
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                ib.WithContent(errorMessage).AddComponents(buttons));
            var resp = await interaction.GetOriginalResponseAsync();


            var result = await interactivity.WaitForButtonAsync(resp, TimeSpan.FromSeconds(60));
            if (result.TimedOut)
            {
                await interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder
                    {
                        Content = "<:attention:1085333468688433232> Du hast nicht rechtzeitig reagiert."
                    });
                return;
            }

            if (result.Result.Id == $"chdel_accept_{caseid}")
            {
                var cid = channel.Id;
                await channel.DeleteAsync();
                await interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder
                    {
                        Content = "<:success:1085333481820790944> Dein Channel wurde gelöscht."
                    });
                await RemoveChannelFromDB(cid);
                return;
            }

            if (result.Result.Id == $"chdel_deny_{caseid}")
                await interaction.EditOriginalResponseAsync(
                    new DiscordWebhookBuilder
                    {
                        Content = "<:attention:1085333468688433232> Vorgang abgebrochen."
                    });
        }
    }

    protected static async Task PanelPermitVoiceSelector(DiscordInteraction interaction, DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!db_channel.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && db_channel.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var channel = interaction.Guild.GetChannel(userChannel.Id);
            var interactivity = client.GetInteractivity();
            var selector = new List<DiscordComponent>
            {
                new DiscordUserSelectComponent("Wähle zuzulassende Mitglieder aus.", "permit_selector",
                    1, 8)
            };
            var message =
                "<:botpoint:1083853403316297758> Um eine Option auszuwählen, verwende das Menü und klicke darauf:";

            if (interaction.Guild.Id == 750365461945778209)
            {
                List<DiscordComponent> button = new()
                {
                    new DiscordButtonComponent(ButtonStyle.Secondary, "role_permit_button",
                        "Levelbeschränkung festlegen")
                };


                DiscordInteractionResponseBuilder ib = new()
                {
                    IsEphemeral = true,
                    Content = message
                };
                List<DiscordActionRowComponent> rowComponents = new()
                {
                    new DiscordActionRowComponent(selector),
                    new DiscordActionRowComponent(button)
                };
                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    ib.AddComponents(rowComponents));
            }
            else
            {
                List<DiscordComponent> button = new()
                {
                    new DiscordButtonComponent(ButtonStyle.Secondary, "role_permit_button",
                        "Levelbeschränkung festlegen")
                };


                DiscordInteractionResponseBuilder ib = new()
                {
                    IsEphemeral = true,
                    Content = message
                };
                List<DiscordActionRowComponent> rowComponents = new()
                {
                    new DiscordActionRowComponent(selector),
                    new DiscordActionRowComponent(button)
                };
                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    ib.AddComponents(rowComponents));
            }
        }
    }

    protected static async Task PanelPermitVoiceSelectorCallback(DiscordInteraction interaction, DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!db_channel.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && db_channel.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var channel = interaction.Guild.GetChannel(userChannel.Id);

            var u = e.Values.ToList();
            var users = e.Values.Select(x => ulong.Parse(x));
            var usersList = new List<DiscordMember>();
            List<ulong> idlist = new();
            var overwrites = channel.PermissionOverwrites.Select(x => x.ConvertToBuilder())
                .ToList();
            foreach (var id in users)
                try
                {
                    idlist.Add(id);
                    if (id == interaction.User.Id) continue;

                    var user = await interaction.Guild.GetMemberAsync(id);


                    overwrites = overwrites.Merge(user, Permissions.AccessChannels | Permissions.UseVoice,
                        Permissions.None);


                    usersList.Add(user);
                }
                catch (NotFoundException)
                {
                    // ignored
                }

            await channel.ModifyAsync(x => { x.PermissionOverwrites = overwrites; });
            await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content =
                        $"<:success:1085333481820790944> {usersList.Count} von {idlist.Count} User {(usersList.Count == 1 ? "wurde" : "wurden")} **permittet**."
                });
        }
    }

    protected static async Task PanelPermitVoiceRole(DiscordInteraction interaction, DiscordClient client,
        InteractionCreateEventArgs e)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!db_channel.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        var ch_locked = false;
        var overwrite = userChannel.PermissionOverwrites.FirstOrDefault(o => o.Id == interaction.Guild.EveryoneRole.Id);
        if (overwrite == null || overwrite?.CheckPermission(Permissions.UseVoice) == PermissionLevel.Unset)
            ch_locked = false;

        if (overwrite?.CheckPermission(Permissions.UseVoice) == PermissionLevel.Denied) ch_locked = true;

        if (!ch_locked)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content =
                        "<:attention:1085333468688433232> Der Channel ist **nicht gesperrt** und eine Levelbegrenzung würde __keinen Effekt__ haben. Bitte **sperre** den Channel zuerst!"
                });
            return;
        }

        if ((userChannel != null && db_channel.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var role_permitted = false;
            var lvlroles = levelroles;
            foreach (var role in interaction.Guild.Roles)
            {
                var RoleId = role.Value.Id;
                if (lvlroles.ContainsKey(RoleId))
                {
                    var temp_ow = userChannel.PermissionOverwrites.FirstOrDefault(o => o.Id == RoleId);
                    if (temp_ow != null)
                    {
                        if (temp_ow?.CheckPermission(Permissions.UseVoice) == PermissionLevel.Allowed)
                        {
                            role_permitted = true;
                            break;
                        }

                        if (temp_ow?.CheckPermission(Permissions.UseVoice) == PermissionLevel.Unset)
                        {
                            role_permitted = false;
                            break;
                        }
                    }
                }
            }

            if (role_permitted)
            {
                await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                    new DiscordInteractionResponseBuilder
                    {
                        IsEphemeral = true,
                        Content =
                            "<:attention:1085333468688433232> Es wurde bereits eine **Levelbegrenzung** für diesen Channel festgelegt."
                    });
                return;
            }

            var options = new List<DiscordStringSelectComponentOption>();

            foreach (var kvp in lvlroles)
            {
                var roleId = kvp.Key;
                var id = kvp.Value;
                options.Add(new DiscordStringSelectComponentOption(id, roleId.ToString()));
            }

            var selectComponent = new DiscordStringSelectComponent
                ("Wähle ein zuzulassendes Level aus.", options, "role_permit_selector");
            var sbuilder = new DiscordInteractionResponseBuilder
            {
                IsEphemeral = true,
                Content =
                    "<:botpoint:1083853403316297758> Um eine Option auszuwählen, verwende das Menü und klicke darauf:"
            };
            sbuilder.AddComponents(selectComponent);
            await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage, sbuilder);
        }
    }

    protected static async Task PanelPermitVoiceRoleCallback(DiscordInteraction interaction, DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!db_channel.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && db_channel.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var channel = userChannel;
            var sel_role = e.Values.ToList();
            var role = interaction.Guild.GetRole(ulong.Parse(sel_role[0]));
            var overwrites = channel.PermissionOverwrites.Select(x => x.ConvertToBuilder())
                .ToList();
            overwrites = overwrites.Merge(role, Permissions.AccessChannels | Permissions.UseVoice, Permissions.None);
            await channel.ModifyAsync(x => { x.PermissionOverwrites = overwrites; });
            await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content =
                        $"<:success:1085333481820790944> Erfolg! Es können nur noch Mitglieder den Kanal betreten, die die Rolle ``{role.Name}`` haben."
                });
        }
    }

    protected static async Task PanelChannelUnpermit(DiscordInteraction interaction, DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!db_channel.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && db_channel.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var channel = userChannel;
            List<ulong> permited_users = new();
            var puserow = userChannel.PermissionOverwrites
                .Where(x => x.CheckPermission(Permissions.UseVoice) == PermissionLevel.Allowed)
                .Where(x => x.Id != interaction.User.Id)
                .Where(x => x.Type == OverwriteType.Member)
                .Select(x => x.Id)
                .ToList();
            foreach (var userid in puserow)
                if (userid != interaction.User.Id)
                {
                    var channelowner = await GetChannelOwnerID(userChannel);
                    if (channelowner == (long)userid) continue;

                    permited_users.Add(userid);
                }

            var allowed_users = permited_users.Count;
            var options = new List<DiscordStringSelectComponentOption>();
            var role_permitted = false;
            var lvlroles = levelroles;
            var roleName = string.Empty;
            foreach (var role in interaction.Guild.Roles)
            {
                var RoleId = role.Value.Id;
                if (lvlroles.ContainsKey(RoleId))
                {
                    var temp_ow = userChannel.PermissionOverwrites.FirstOrDefault(o => o.Id == RoleId);
                    if (temp_ow != null)
                    {
                        roleName = lvlroles[RoleId];
                        role_permitted = true;
                        break;
                    }
                }
            }

            foreach (var uid in permited_users)
            {
                var user = await interaction.Guild.GetMemberAsync(uid);
                var username = user.DisplayName;
                options.Add(new DiscordStringSelectComponentOption(username, uid.ToString(),
                    emoji: new DiscordComponentEmoji(1083853403316297758)));
            }

            if (allowed_users == 0)
            {
                var content = "<:attention:1085333468688433232> Es sind __keine__ Mitglieder **permittet**!";
                var sbuilder = new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content = content
                };

                if (role_permitted)
                {
                    List<DiscordButtonComponent> buttons = new()
                    {
                        new DiscordButtonComponent(ButtonStyle.Primary, "unpermit_levelrole",
                            $"Entferne zugelassene Levelrolle ({roleName})")
                    };
                    sbuilder.AddComponents(buttons);
                }

                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, sbuilder);
                return;
            }

            if (allowed_users > 25)
            {
                var content =
                    $"<:attention:1085333468688433232> Es sind __zu viele__ Mitglieder **permittet**! Bitte benutze den ``{BotConfig.GetConfig()["MainConfig"]["BotPrefix"]}unpermit`` Command.";
                var sbuilder = new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content = content
                };
                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, sbuilder);
                return;
            }

            var ul = 10;
            if (allowed_users < 10) ul = allowed_users;

            var selector = new List<DiscordComponent>
            {
                new DiscordStringSelectComponent
                ("Wähle zu entfernende Mitglieder aus.",
                    options, "unpermit_selector", maxOptions: ul)
            };
            List<DiscordActionRowComponent> rowComponents = new()
            {
                new DiscordActionRowComponent(selector)
            };
            if (role_permitted)
            {
                List<DiscordButtonComponent> buttons = new()
                {
                    new DiscordButtonComponent(ButtonStyle.Danger, "unpermit_levelrole",
                        $"Entferne zugelassene Levelrolle ({roleName})")
                };
                rowComponents.Add(new DiscordActionRowComponent(buttons));
            }

            var econtent =
                "<:botpoint:1083853403316297758> Um eine Option auszuwählen, verwende das Menü und klicke darauf:";
            var ssbuilder = new DiscordInteractionResponseBuilder
            {
                IsEphemeral = true,
                Content = econtent
            };
            ssbuilder.AddComponents(rowComponents);
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, ssbuilder);
        }
    }

    protected static async Task PanelChannelUnpermitRoleCallback(DiscordInteraction interaction, DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!db_channel.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && db_channel.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var channel = userChannel;
            ulong r_id = 0;
            var lvlroles = levelroles;
            foreach (var role in interaction.Guild.Roles)
            {
                var RoleId = role.Value.Id;
                if (lvlroles.ContainsKey(RoleId))
                {
                    var temp_ow = userChannel.PermissionOverwrites.FirstOrDefault(o => o.Id == RoleId);
                    if (temp_ow != null)
                    {
                        r_id = RoleId;
                        break;
                    }
                }
            }

            var role_ = interaction.Guild.GetRole(r_id);
            var overwrites = userChannel.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();
            overwrites = overwrites.Merge(role_, Permissions.None, Permissions.None,
                Permissions.UseVoice | Permissions.AccessChannels);
            await channel.ModifyAsync(x => x.PermissionOverwrites = overwrites);
            var content = "<:success:1085333481820790944> Erfolg! Die Levelbeschränkung wurde **aufgehoben**.";
            var sbuilder = new DiscordInteractionResponseBuilder
            {
                IsEphemeral = true,
                Content = content
            };
            await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage, sbuilder);
        }
    }

    protected static async Task PanelChannelUnpermitUserCallback(DiscordInteraction interaction, DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!db_channel.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && db_channel.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var channel = userChannel;
            var u = e.Values.ToList();
            var users = e.Values.Select(x => ulong.Parse(x));
            var usersList = new List<DiscordMember>();
            List<ulong> idlist = new();
            var overwrites = channel.PermissionOverwrites.Select(x => x.ConvertToBuilder())
                .ToList();
            foreach (var id in users)
                try
                {
                    idlist.Add(id);
                    if (id == interaction.User.Id) continue;

                    var user = await interaction.Guild.GetMemberAsync(id);


                    overwrites = overwrites.Merge(user, Permissions.None,
                        Permissions.None, Permissions.AccessChannels | Permissions.UseVoice);


                    usersList.Add(user);
                }
                catch (NotFoundException)
                {
                    // ignored
                }

            await channel.ModifyAsync(x => { x.PermissionOverwrites = overwrites; });
            await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content =
                        $"<:success:1085333481820790944> {usersList.Count} von {idlist.Count} User {(usersList.Count == 1 ? "wurde" : "wurden")} **unpermitted**."
                });
        }
    }

    protected static async Task PanelChannelClaim(DiscordInteraction interaction, DiscordClient client)
    {
        var dbChannels = await GetChannelIDFromDB(interaction);
        var all_dbChannels = await GetAllChannelIDsFromDB();
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var channelownerid = await GetChannelOwnerID(interaction);
        if (channelownerid == (long)interaction.User.Id)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content = "<:attention:1085333468688433232> Du bist bereits der Channelowner."
                });
            return;
        }

        if (userChannel == null)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content = "<:attention:1085333468688433232> Du bist in keinem Voice-Channel."
                });
            return;
        }

        if (channelownerid == null)
        {
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content =
                        "<:attention:1085333468688433232> Dieser Channel ist nicht claimbar. Du musst dich in einem Temp-VC Channel befinden"
                });
            return;
        }

        var channelowner = await client.GetUserAsync((ulong)channelownerid);
        var channelownermember = await interaction.Guild.GetMemberAsync(channelowner.Id);
        var orig_owner = channelownermember;
        var new_owner = member;
        var channel = userChannel;
        var overwrites = userChannel.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();

        if (!channel.Users.Contains(orig_owner) && all_dbChannels.Contains((long)userChannel.Id))
        {
            await using (NpgsqlConnection conn = new(DatabaseService.GetConnectionString()))
            {
                await conn.OpenAsync();
                var sql = "UPDATE tempvoice SET ownerid = @owner WHERE channelid = @channelid";
                await using (NpgsqlCommand command = new(sql, conn))
                {
                    command.Parameters.AddWithValue("@owner", (long)new_owner.Id);
                    command.Parameters.AddWithValue("@channelid", (long)channel.Id);
                    var affected = await command.ExecuteNonQueryAsync();
                }
            }

            overwrites = overwrites.Merge(orig_owner, Permissions.None, Permissions.None,
                Permissions.ManageChannels | Permissions.UseVoice | Permissions.MoveMembers |
                Permissions.AccessChannels);
            overwrites = overwrites.Merge(new_owner,
                Permissions.ManageChannels | Permissions.UseVoice | Permissions.MoveMembers |
                Permissions.AccessChannels, Permissions.None);

            await ResetChannelMods(channel);
            await userChannel.ModifyAsync(x => x.PermissionOverwrites = overwrites);
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content = "<:success:1085333481820790944> Erfolg! Du bist jetzt der Channelowner."
                });
            return;
        }

        if (channel.Users.Contains(orig_owner) && all_dbChannels.Contains((long)userChannel.Id))
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content =
                        $"<:attention:1085333468688433232> Dieser Channel ist nicht claimbar. Der Channelowner {orig_owner.UsernameWithDiscriminator} {orig_owner.Mention} befindet sich noch im Channel"
                });
    }

    protected static async Task PanelChannelTransfer(DiscordInteraction interaction)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        if (userChannel == null || !db_channel.Contains((long)userChannel?.Id))
        {
            await NoChannel(interaction);
            return;
        }

        if (userChannel != null && db_channel.Contains((long)userChannel.Id))
        {
            List<ulong> UsersInChannel = new();
            foreach (var user in userChannel.Users)
                if (user.Id != interaction.User.Id)
                    UsersInChannel.Add(user.Id);

            var options = new List<DiscordStringSelectComponentOption>();
            foreach (var uid in UsersInChannel)
            {
                var user = await interaction.Guild.GetMemberAsync(uid);
                options.Add(new DiscordStringSelectComponentOption(user.UsernameWithDiscriminator, user.Id.ToString(),
                    emoji: new DiscordComponentEmoji(1083853403316297758)));
            }

            if (UsersInChannel.Count == 0)
            {
                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder
                    {
                        IsEphemeral = true,
                        Content = "<:attention:1085333468688433232> Es befinden sich keine User in deinem Channel."
                    });
                return;
            }

            if (UsersInChannel.Count > 25)
            {
                var content =
                    $"<:attention:1085333468688433232> Es sind __zu viele__ Mitglieder **permittet**! Bitte benutze den ``{BotConfig.GetConfig()["MainConfig"]["BotPrefix"]}unpermit`` Command.";
                var sbuilder = new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content = content
                };
                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, sbuilder);
                return;
            }

            var selector = new List<DiscordComponent>
            {
                new DiscordStringSelectComponent("Wähle den Zieluser aus", options, "transfer_selector",
                    maxOptions: 1)
            };
            List<DiscordActionRowComponent> rowComponents = new()
            {
                new DiscordActionRowComponent(selector)
            };
            var econtent =
                "<:botpoint:1083853403316297758> Um eine Option auszuwählen, verwende das Menü und klicke darauf:";
            var ssbuilder = new DiscordInteractionResponseBuilder
            {
                IsEphemeral = true,
                Content = econtent
            };
            ssbuilder.AddComponents(rowComponents);
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, ssbuilder);
        }
    }

    protected static async Task PanelChannelTransferCallback(DiscordInteraction interaction, DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var orig_owner = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = orig_owner?.VoiceState?.Channel;
        if (userChannel == null || !db_channel.Contains((long)userChannel?.Id))
        {
            await NoChannel(interaction);
            return;
        }

        var channelownerid = await GetChannelOwnerID(interaction);
        if (userChannel != null && db_channel.Contains((long)userChannel.Id))
        {
            var channel = userChannel;
            var n_memberid = ulong.Parse(e.Values.First());
            var new_owner = await interaction.Guild.GetMemberAsync(n_memberid);
            var overwrites = userChannel.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();
            if (channel.Users.Contains(orig_owner) && db_channel.Contains((long)userChannel.Id))
            {
                await using (NpgsqlConnection conn = new(DatabaseService.GetConnectionString()))
                {
                    await conn.OpenAsync();
                    var sql = "UPDATE tempvoice SET ownerid = @owner WHERE channelid = @channelid";
                    await using (NpgsqlCommand command = new(sql, conn))
                    {
                        command.Parameters.AddWithValue("@owner", (long)new_owner.Id);
                        command.Parameters.AddWithValue("@channelid", (long)channel.Id);
                        var affected = await command.ExecuteNonQueryAsync();
                    }
                }

                await ResetChannelMods(channel);
                overwrites = overwrites.Merge(orig_owner, Permissions.None, Permissions.None,
                    Permissions.ManageChannels | Permissions.MoveMembers);
                overwrites = overwrites.Merge(new_owner,
                    Permissions.ManageChannels | Permissions.UseVoice | Permissions.MoveMembers |
                    Permissions.AccessChannels, Permissions.None);
                await userChannel.ModifyAsync(x => x.PermissionOverwrites = overwrites);
                await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                    new DiscordInteractionResponseBuilder
                    {
                        IsEphemeral = true,
                        Content =
                            $"<:success:1085333481820790944> Du hast den Channel erfolgreich an {new_owner.Mention} **übertragen**."
                    });
                return;
            }

            if (channel.Users.Contains(orig_owner) && db_channel.Contains((long)userChannel.Id) &&
                channel.Users.Contains(new_owner))
                await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                    new DiscordInteractionResponseBuilder
                    {
                        IsEphemeral = true,
                        Content = $"<:attention:1085333468688433232> {new_owner.Mention} ist __nicht__ im Kanal!"
                    });
        }
    }

    protected static async Task PanelChannelKick(DiscordInteraction interaction)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!db_channel.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && db_channel.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            List<ulong> ChUsers = new();
            foreach (var chuser in userChannel.Users)
            {
                var uid = chuser.Id;
                var mods = await RetrieveChannelMods(userChannel);
                if (uid != interaction.User.Id && !mods.Contains(uid)) ChUsers.Add(uid);
            }

            var options = new List<DiscordStringSelectComponentOption>();

            foreach (var uid in ChUsers)
            {
                var user = await interaction.Guild.GetMemberAsync(uid);
                var username = user.DisplayName;
                options.Add(new DiscordStringSelectComponentOption(username, uid.ToString(),
                    emoji: new DiscordComponentEmoji(1083853403316297758)));
            }

            if (ChUsers.Count == 0)
            {
                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder
                    {
                        IsEphemeral = true,
                        Content = "<:attention:1085333468688433232> Es befinden sich keine User in deinem Channel."
                    });
                return;
            }

            if (ChUsers.Count > 25)
            {
                var content =
                    $"<:attention:1085333468688433232> Es sind __zu viele__ Mitglieder im Channel! Bitte benutze den ``{BotConfig.GetConfig()["MainConfig"]["BotPrefix"]}vckick`` Command.";
                var sbuilder = new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content = content
                };
                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, sbuilder);
                return;
            }

            var selector = new List<DiscordComponent>
            {
                new DiscordStringSelectComponent("Wähle den Zieluser aus", options, "kick_selector",
                    maxOptions: 1)
            };
            List<DiscordActionRowComponent> rowComponents = new()
            {
                new DiscordActionRowComponent(selector)
            };
            var econtent =
                "<:botpoint:1083853403316297758> Um eine Option auszuwählen, verwende das Menü und klicke darauf:";
            var ssbuilder = new DiscordInteractionResponseBuilder
            {
                IsEphemeral = true,
                Content = econtent
            };
            ssbuilder.AddComponents(rowComponents);
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, ssbuilder);
        }
    }

    protected static async Task PanelChannelKickCallback(DiscordInteraction interaction, DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!db_channel.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && db_channel.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var kickuserid_str = e.Values.First();
            var kickuserid = ulong.Parse(kickuserid_str);
            var kickuser = await interaction.Guild.GetMemberAsync(kickuserid);
            var kickuserchannel = kickuser?.VoiceState?.Channel;
            if (kickuserchannel != null && kickuserchannel == userChannel)
            {
                await kickuser.DisconnectFromVoiceAsync();
                await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                    new DiscordInteractionResponseBuilder
                    {
                        IsEphemeral = true,
                        Content =
                            $"<:success:1085333481820790944> {kickuser.Mention} wurde erfolgreich aus dem Channel gekickt."
                    });
            }
        }
    }

    protected static async Task PanelChannelBlock(DiscordInteraction interaction)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!db_channel.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && db_channel.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var channel = interaction.Guild.GetChannel(userChannel.Id);
            var selector = new List<DiscordComponent>
            {
                new DiscordUserSelectComponent("Wähle zu blockierende Mitglieder aus.", "ban_selector",
                    1, 8)
            };
            var message =
                "<:botpoint:1083853403316297758> Um eine Option auszuwählen, verwende das Menü und klicke darauf:";


            DiscordInteractionResponseBuilder ib = new()
            {
                IsEphemeral = true,
                Content = message
            };
            List<DiscordActionRowComponent> rowComponents = new()
            {
                new DiscordActionRowComponent(selector)
            };
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                ib.AddComponents(rowComponents));
        }
    }

    protected static async Task PanelChannelBlockCallback(DiscordInteraction interaction, DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!db_channel.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && db_channel.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var channel = interaction.Guild.GetChannel(userChannel.Id);

            var u = e.Values.ToList();
            var users = e.Values.Select(x => ulong.Parse(x));
            var usersList = new List<DiscordMember>();
            List<ulong> idlist = new();
            var overwrites = channel.PermissionOverwrites.Select(x => x.ConvertToBuilder())
                .ToList();
            var staffrole = interaction.Guild.GetRole(GlobalProperties.StaffRoleId);

            foreach (var id in users)
                try
                {
                    idlist.Add(id);
                    var mods = await RetrieveChannelMods(userChannel);
                    if (id == interaction.User.Id || mods.Contains(id)) continue;

                    if (interaction.Guild.Id == ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"]))
                        if (staffrole.Members.Any(x => x.Value.Id == id))
                            continue;


                    var user = await interaction.Guild.GetMemberAsync(id);
                    try
                    {
                        var currentmods = await RetrieveChannelMods(userChannel);
                        currentmods.Remove(user.Id);
                        await UpdateChannelMods(userChannel, currentmods);
                    }
                    catch (Exception)
                    {
                    }

                    //overwrites = overwrites.Merge(user, Permissions.None, Permissions.None, Permissions.UseVoice | Permissions.AccessChannels);
                    overwrites = overwrites.Merge(user, Permissions.None, Permissions.UseVoice,
                        Permissions.AccessChannels);
                    if (userChannel.Users.Contains(user)) await user.DisconnectFromVoiceAsync();

                    usersList.Add(user);
                }
                catch (NotFoundException)
                {
                    // ignored
                }

            await channel.ModifyAsync(x => { x.PermissionOverwrites = overwrites; });
            await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content =
                        $"<:success:1085333481820790944> {usersList.Count} von {idlist.Count} User {(usersList.Count == 1 ? "wurde" : "wurden")} **blockiert**."
                });
        }
    }

    protected static async Task PanelChannelUnblock(DiscordInteraction interaction)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!db_channel.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && db_channel.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var channel = userChannel;
            List<ulong> permited_users = new();
            var puserow = userChannel.PermissionOverwrites
                .Where(x => x.CheckPermission(Permissions.UseVoice) == PermissionLevel.Denied)
                .Where(x => x.Id != interaction.User.Id)
                .Where(x => x.Type == OverwriteType.Member)
                .Select(x => x.Id)
                .ToList();
            foreach (var userid in puserow)
                if (userid != interaction.User.Id)
                    permited_users.Add(userid);

            var blocked_users = permited_users.Count;
            var options = new List<DiscordStringSelectComponentOption>();
            var lvlroles = levelroles;
            var roleName = string.Empty;
            foreach (var role in interaction.Guild.Roles)
            {
                var RoleId = role.Value.Id;
                if (lvlroles.ContainsKey(RoleId))
                {
                    var temp_ow = userChannel.PermissionOverwrites.FirstOrDefault(o => o.Id == RoleId);
                    if (temp_ow != null)
                    {
                        roleName = lvlroles[RoleId];
                        break;
                    }
                }
            }

            foreach (var uid in permited_users)
            {
                var user = await interaction.Guild.GetMemberAsync(uid);
                var username = user.DisplayName;
                options.Add(new DiscordStringSelectComponentOption(username, uid.ToString(),
                    emoji: new DiscordComponentEmoji(1083853403316297758)));
            }

            if (blocked_users == 0)
            {
                var content = "<:attention:1085333468688433232> Es sind __keine__ Mitglieder **blockiert**!";
                var sbuilder = new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content = content
                };
                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, sbuilder);
                return;
            }

            if (blocked_users > 25)
            {
                var content =
                    $"<:attention:1085333468688433232> Es sind __zu viele__ Mitglieder **blockiert**! Bitte benutze den ``{BotConfig.GetConfig()["MainConfig"]["BotPrefix"]}unblock`` Command.";
                var sbuilder = new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content = content
                };
                await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, sbuilder);
                return;
            }

            var ul = 10;
            if (blocked_users < 10) ul = blocked_users;

            var selector = new List<DiscordComponent>
            {
                new DiscordStringSelectComponent
                ("Wähle zu entblockierende Mitglieder aus.",
                    options, "unban_selector", maxOptions: ul)
            };
            List<DiscordActionRowComponent> rowComponents = new()
            {
                new DiscordActionRowComponent(selector)
            };

            var econtent =
                "<:botpoint:1083853403316297758> Um eine Option auszuwählen, verwende das Menü und klicke darauf:";
            var ssbuilder = new DiscordInteractionResponseBuilder
            {
                IsEphemeral = true,
                Content = econtent
            };
            ssbuilder.AddComponents(rowComponents);
            await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, ssbuilder);
        }
    }

    protected static async Task PanelChannelUnblockCallback(DiscordInteraction interaction, DiscordClient client,
        ComponentInteractionCreateEventArgs e)
    {
        var db_channel = await GetChannelIDFromDB(interaction);
        var member = await interaction.Guild.GetMemberAsync(interaction.User.Id);
        var userChannel = member?.VoiceState?.Channel;
        var isMod = await IsChannelMod(userChannel, interaction.User);

        if (userChannel == null || (!db_channel.Contains((long)userChannel?.Id) && !isMod))
        {
            await NoChannel(interaction);
            return;
        }

        if ((userChannel != null && db_channel.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
        {
            var channel = userChannel;
            var u = e.Values.ToList();
            var users = e.Values.Select(x => ulong.Parse(x));
            var usersList = new List<DiscordMember>();
            List<ulong> idlist = new();
            var overwrites = channel.PermissionOverwrites.Select(x => x.ConvertToBuilder())
                .ToList();
            foreach (var id in users)
                try
                {
                    idlist.Add(id);
                    if (id == interaction.User.Id) continue;

                    var user = await interaction.Guild.GetMemberAsync(id);


                    overwrites = overwrites.Merge(user, Permissions.None, Permissions.None, Permissions.UseVoice);


                    usersList.Add(user);
                }
                catch (NotFoundException)
                {
                    // ignored
                }

            await channel.ModifyAsync(x => { x.PermissionOverwrites = overwrites; });
            await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder
                {
                    IsEphemeral = true,
                    Content =
                        $"<:success:1085333481820790944> {usersList.Count} von {idlist.Count} User {(usersList.Count == 1 ? "wurde" : "wurden")} **entblockiert**."
                });
        }
    }

    protected static bool GetSoundboardState(DiscordChannel channel)
    {
        var active = false;
        var overwrite =
            channel.PermissionOverwrites.FirstOrDefault(o => o.Id == channel.Guild.EveryoneRole.Id);
        if (overwrite?.CheckPermission(Permissions.UseSoundboard) == PermissionLevel.Allowed) active = true;

        if (overwrite == null || overwrite?.CheckPermission(Permissions.UseSoundboard) == PermissionLevel.Unset)
            active = false;

        return active;
    }

    protected static async Task SetSoundboardState(DiscordChannel channel, bool state)
    {
        if (state)
        {
            var overwrites = channel.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();
            overwrites = overwrites.Merge(channel.Guild.EveryoneRole,
                Permissions.UseExternalSounds | Permissions.UseSoundboard, Permissions.None);
            await channel.ModifyAsync(x => x.PermissionOverwrites = overwrites);
            return;
        }

        if (!state)
        {
            var overwrites = channel.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();
            overwrites = overwrites.Merge(channel.Guild.EveryoneRole, Permissions.None, Permissions.None,
                Permissions.UseExternalSounds | Permissions.UseSoundboard);
            await channel.ModifyAsync(x => x.PermissionOverwrites = overwrites);
        }
    }
}