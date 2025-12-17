#region

using AGC_Management.Attributes;
using AGC_Management.Services;
using AGC_Management.TempVoice;

#endregion

namespace AGC_Management.Commands.TempVC;

public sealed class ClaimChannelCommand : TempVoiceHelper
{
    [Command("claim")]
    [RequireDatabase]
    [Aliases("claimvc")]
    public async Task ClaimVoice(CommandContext ctx)
    {
        _ = Task.Run(async () =>
        {
            var dbChannels = await GetChannelIDFromDB(ctx);
            var all_dbChannels = await GetAllChannelIDsFromDB();
            var userChannel = ctx.Member?.VoiceState?.Channel;
            var channelownerid = await GetChannelOwnerID(ctx);
            var msg = await ctx.RespondAsync(
                "<a:loading_agc:1084157150747697203> **Lade...** Versuche Channel zu übernehmen...");
            if (channelownerid == (long)ctx.User.Id)
            {
                await msg.ModifyAsync("<:attention:1085333468688433232> Du bist bereits der Besitzer des Channels.");
                return;
            }

            if (ctx.Member.VoiceState?.Channel == null)
            {
                await msg.ModifyAsync("<:attention:1085333468688433232> Du bist in keinem Voice-Channel.");
                return;
            }

            if (channelownerid == null)
            {
                await msg.ModifyAsync("<:attention:1085333468688433232> Du bist in keinem TempVC Channel.");
                return;
            }

            var channelowner = await ctx.Client.GetUserAsync((ulong)channelownerid);
            var channelownermember = await ctx.Guild.GetMemberAsync(channelowner.Id);
            var orig_owner = channelownermember;
            var new_owner = ctx.Member;
            var channel = ctx.Member.VoiceState?.Channel;
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
                await msg.ModifyAsync("<:success:1085333481820790944> Du hast den Channel erfolgreich **geclaimt**!");
            }

            if (channel.Users.Contains(orig_owner) && all_dbChannels.Contains((long)userChannel.Id))
                await msg.ModifyAsync(
                    $"<:attention:1085333468688433232> Du kannst dein Channel nicht Claimen, da der Channel-Owner ``{orig_owner.UsernameWithDiscriminator}`` noch im Channel ist.");
        });
    }
}