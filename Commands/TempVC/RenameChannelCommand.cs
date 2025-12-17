#region

using AGC_Management.Attributes;
using AGC_Management.Services;
using AGC_Management.TempVoice;

#endregion

namespace AGC_Management.Commands.TempVC;

public sealed class RenameChannelCommand : TempVoiceHelper
{
    [Command("rename")]
    [RequireDatabase]
    [Aliases("vcname")]
    public async Task VoiceRename(CommandContext ctx, [RemainingText] string name)
    {
        _ = Task.Run(async () =>
        {
            if (name.Length == 0 || name.Length > 25)
            {
                await ctx.RespondAsync(
                    "<:attention:1085333468688433232> **Fehler!** Der Name muss zwischen 1 und 25 Zeichen lang sein.");
                return;
            }

            var current_timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
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
                    "<a:loading_agc:1084157150747697203> **Lade...** Versuche Channel umzubenennen...");
                var channel = ctx.Member.VoiceState.Channel;
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
                    await msg.ModifyAsync(
                        $"<:attention:1085333468688433232> **Fehler!** Der Channel wurde in den letzten 5 Minuten schon einmal umbenannt. Bitte warte noch etwas, bevor du den Channel erneut umbenennen kannst. __Beachte:__ Auf diese Aktualisierung haben wir keinen Einfluss und dies Betrifft nur Bots. Erneut umbenennen kannst du den Channel <t:{calc}:R>.");
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
                        var affected = await command.ExecuteNonQueryAsync();
                    }
                }

                await msg.ModifyAsync(
                    "<:success:1085333481820790944> **Erfolg!** Der Channel wurde erfolgreich umbenannt.");
            }
        });
    }
}