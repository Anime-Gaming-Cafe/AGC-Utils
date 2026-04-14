#region

using AGC_Management.Attributes;
using AGC_Management.Services;
using AGC_Management.TempVoice;

#endregion

namespace AGC_Management.Commands.TempVC;

[Group("channelstatus")]
[Aliases("vcstatus", "channel-status", "vc-status")]
[RequireDatabase]
public sealed class ChannelStatusCommands : TempVoiceHelper
{
    [Command("set")]
    public async Task SetStatus(CommandContext ctx, [RemainingText] string text)
    {
        _ = Task.Run(async () =>
        {
            if (text.Length == 0 || text.Length > 250)
            {
                await ctx.RespondAsync(
                    "<:attention:1085333468688433232> **Fehler!** Der Name muss zwischen 1 und 250 Zeichen lang sein.");
                return;
            }

            var current_timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var dbChannels = await GetChannelIDFromDB(ctx);
            var userChannel = ctx.Member?.VoiceState?.Channel;
            var isMod = await IsChannelMod(userChannel, ctx.Member);

            if (userChannel == null || (!dbChannels.Contains((long)userChannel?.Id) && !isMod))
            {
                await NoChannel(ctx);
                return;
            }

            if ((userChannel != null && dbChannels.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
            {
                var msg = await ctx.RespondAsync(
                    "<a:loading_agc:1084157150747697203> **Lade...** Versuche Channel-Status zu setzen...");
                var channel = userChannel;
                long? timestampdata = 0;
                List<string> Query =
				[
					"statuslastedited"
                ];
                Dictionary<string, object> WhereCondiditons = new()
                {
                    { "channelid", (long)channel.Id }
                };
                var dbtimestampdata =
                    await DatabaseService.SelectDataFromTable("tempvoice", Query, WhereCondiditons);
                // look if it is NULL
                foreach (var data in dbtimestampdata)
                    try
                    {
                        timestampdata = (long?)data["statuslastedited"];
                    }
                    catch (InvalidCastException)
                    {
                        timestampdata = null;
                    }

                if (timestampdata is null)
                {
                    await ctx.RespondAsync(
                        "<:attention:1085333468688433232> **Fehler!** Channelstatus ist nicht mit diesem Channel kompatibel. Bitte erstelle einen neuen Channel.");
                    return;
                }

                var edittimestamp = timestampdata;
                var math = current_timestamp - edittimestamp;

                if (math < 60)
                {
                    var calc = edittimestamp + 60;
                    await msg.ModifyAsync(
                        $"<:attention:1085333468688433232> **Fehler!** Der Channelstatus wurde in der letzten Minute schon einmal geändert. Bitte warte noch etwas. Erneut umbenennen kannst du den Channelstatus <t:{calc}:R>.");
                    return;
                }

                try
                {
                    channel.SetVoiceChannelStatusAsync(text);
                }
                catch (Exception)
                {
                    await msg.ModifyAsync(
                        "<:attention:1085333468688433232> **Fehler!** Der Channelstatus konnte nicht geändert werden. Bitte versuche es erneut.");
                    return;
                }


                await using (NpgsqlConnection conn = new(DatabaseService.GetConnectionString()))
                {
                    await conn.OpenAsync();
                    var sql = "UPDATE tempvoice SET statuslastedited = @timestamp WHERE channelid = @channelid";
					await using NpgsqlCommand command = new(sql, conn);
					command.Parameters.AddWithValue("@timestamp", current_timestamp);
					command.Parameters.AddWithValue("@channelid", (long)channel.Id);
					var affected = await command.ExecuteNonQueryAsync();
				}

                await msg.ModifyAsync(
                    "<:success:1085333481820790944> **Erfolg!** Der Channelstatus wurde erfolgreich geändert.");
            }
        });
    }

    [Command("remove")]
    public async Task SetStatus(CommandContext ctx)
    {
        _ = Task.Run(async () =>
        {
            var current_timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var dbChannels = await GetChannelIDFromDB(ctx);
            var userChannel = ctx.Member?.VoiceState?.Channel;
            var isMod = await IsChannelMod(userChannel, ctx.Member);

            if (userChannel == null || (!dbChannels.Contains((long)userChannel?.Id) && !isMod))
            {
                await NoChannel(ctx);
                return;
            }

            if ((userChannel != null && dbChannels.Contains((long)userChannel.Id)) || (userChannel != null && isMod))
            {
                var msg = await ctx.RespondAsync(
                    "<a:loading_agc:1084157150747697203> **Lade...** Versuche Channel-Status zu entfernen...");
                var channel = userChannel;
                long? timestampdata = 0;
                List<string> Query =
				[
					"statuslastedited"
                ];
                Dictionary<string, object> WhereCondiditons = new()
                {
                    { "channelid", (long)channel.Id }
                };
                var dbtimestampdata =
                    await DatabaseService.SelectDataFromTable("tempvoice", Query, WhereCondiditons);
                // look if it is NULL
                foreach (var data in dbtimestampdata)
                    try
                    {
                        timestampdata = (long?)data["statuslastedited"];
                    }
                    catch (InvalidCastException)
                    {
                        timestampdata = null;
                    }

                if (timestampdata is null)
                {
                    await ctx.RespondAsync(
                        "<:attention:1085333468688433232> **Fehler!** Channelstatus ist nicht mit diesem Channel kompatibel. Bitte erstelle einen neuen Channel.");
                    return;
                }

                if (timestampdata == 0)
                {
                    await ctx.RespondAsync(
                        "<:attention:1085333468688433232> **Fehler!** Der Channelstatus wurde noch nicht gesetzt.");
                    return;
                }

                var edittimestamp = timestampdata;
                var math = current_timestamp - edittimestamp;

                if (math < 60)
                {
                    var calc = edittimestamp + 60;
                    await msg.ModifyAsync(
                        $"<:attention:1085333468688433232> **Fehler!** Der Channelstatus wurde in der letzten Minute schon einmal verändert. Bitte warte noch etwas. Ratelimited bis <t:{calc}:R>.");
                    return;
                }

                channel.RemoveVoiceChannelStatusAsync();

                await using (NpgsqlConnection conn = new(DatabaseService.GetConnectionString()))
                {
                    await conn.OpenAsync();
                    var sql = "UPDATE tempvoice SET statuslastedited = @timestamp WHERE channelid = @channelid";
					await using NpgsqlCommand command = new(sql, conn);
					command.Parameters.AddWithValue("@timestamp", current_timestamp);
					command.Parameters.AddWithValue("@channelid", (long)channel.Id);
					var affected = await command.ExecuteNonQueryAsync();
				}

                await msg.ModifyAsync(
                    "<:success:1085333481820790944> **Erfolg!** Der Channelstatus wurde erfolgreich entfernt.");
            }
        });
    }
}