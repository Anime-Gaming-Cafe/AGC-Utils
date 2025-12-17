#region

using AGC_Management.Attributes;
using AGC_Management.TempVoice;
using AGC_Management.Utils;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Commands.TempVC;

public sealed class PermitCommand : TempVoiceHelper
{
    [Command("permit")]
    [RequireDatabase]
    [Aliases("allow", "whitelist", "multipermit")]
    public async Task VoicePermit(CommandContext ctx, [RemainingText] string users)
    {
        _ = Task.Run(async () =>
            {
                var dbChannels = await GetChannelIDFromDB(ctx);
                var userChannel = ctx.Member?.VoiceState?.Channel;
                var isMod = await IsChannelMod(userChannel, ctx.Member);
                if (userChannel == null || (!dbChannels.Contains((long)userChannel?.Id) && !isMod))
                {
                    await NoChannel(ctx);
                    return;
                }

                if ((userChannel != null && dbChannels.Contains((long)userChannel.Id)) ||
                    (userChannel != null && isMod))
                {
                    var permitusers = new List<ulong>();
                    List<ulong> ids = new();
                    ids = Converter.ExtractUserIDsFromString(users);
                    var staffrole = ctx.Guild.GetRole(GlobalProperties.StaffRoleId);
                    var msg = await ctx.RespondAsync(
                        $"<a:loading_agc:1084157150747697203> **Lade...** Versuche {ids.Count} Nutzer zuzulassen...");
                    var overwrites = userChannel.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();

                    foreach (var id in ids)
                        try
                        {
                            var user = await ctx.Guild.GetMemberAsync(id);

                            var channelmods = await RetrieveChannelMods(userChannel);
                            if (channelmods.Contains(user.Id))
                            {
                                var buserow = userChannel.PermissionOverwrites
                                    .Where(x => x.Type == OverwriteType.Member)
                                    .Where(x => x.CheckPermission(Permissions.UseVoice) == PermissionLevel.Denied)
                                    .Select(x => x.Id)
                                    .ToList();

                                if (buserow.Contains(user.Id)) continue;
                            }

                            overwrites = overwrites.Merge(user, Permissions.AccessChannels | Permissions.UseVoice,
                                Permissions.None);


                            permitusers.Add(user.Id);
                        }
                        catch (NotFoundException)
                        {
                        }

                    await userChannel.ModifyAsync(x => x.PermissionOverwrites = overwrites);

                    var successCount = permitusers.Count;
                    var endstring =
                        $"<:success:1085333481820790944> **Erfolg!** Es {(successCount == 1 ? "wurde" : "wurden")} {successCount} Nutzer erfolgreich **zugelassen**!";

                    await msg.ModifyAsync(endstring);
                }
            }
        );
    }
}