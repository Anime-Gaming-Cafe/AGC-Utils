﻿using AGC_Management.Attributes;
using AGC_Management.Utils;
using AGC_Management.Utils.TempVoice;
using DisCatSharp.CommandsNext;
using DisCatSharp.CommandsNext.Attributes;
using DisCatSharp.Entities;
using DisCatSharp.Enums;
using DisCatSharp.Exceptions;

namespace AGC_Management.Commands.TempVC;

public sealed class UnpermitCommand : TempVoiceHelper
{
    [Command("unpermit")]
    [RequireDatabase]
    [Aliases("unwhitelist")]
    public async Task VoiceUnpermit(CommandContext ctx, [RemainingText] string users)
    {
        _ = Task.Run(async () =>
            {
                List<long> dbChannels = await GetChannelIDFromDB(ctx);
                DiscordChannel userChannel = ctx.Member?.VoiceState?.Channel;

                bool isMod = await IsChannelMod(userChannel, ctx.Member);

                if (userChannel == null || !dbChannels.Contains((long)userChannel?.Id) && !isMod)
                {
                    await NoChannel(ctx);
                    return;
                }

                if (userChannel != null && dbChannels.Contains((long)userChannel.Id) || userChannel != null && isMod)
                {
                    var unpermitlist = new List<ulong>();
                    List<ulong> ids = new();
                    ids = Converter.ExtractUserIDsFromString(users);
                    var staffrole = ctx.Guild.GetRole(GlobalProperties.StaffRoleId);
                    var msg = await ctx.RespondAsync(
                        $"<a:loading_agc:1084157150747697203> **Lade...** Versuche {ids.Count} Nutzer unzupermitten...");
                    var overwrites = userChannel.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();

                    foreach (ulong id in ids)
                    {
                        try
                        {
                            var user = await ctx.Guild.GetMemberAsync(id);

                            var channelowner = await GetChannelOwnerID(userChannel);
                            if (channelowner == (long)user.Id)
                            {
                                continue;
                            }

                            overwrites = overwrites.Merge(user, Permissions.None,
                                Permissions.None, Permissions.AccessChannels | Permissions.UseVoice);


                            unpermitlist.Add(user.Id);
                        }
                        catch (NotFoundException)
                        {
                        }
                    }

                    await userChannel.ModifyAsync(x => x.PermissionOverwrites = overwrites);

                    int successCount = unpermitlist.Count;
                    string endstring =
                        $"<:success:1085333481820790944> **Erfolg!** Es {(successCount == 1 ? "wurde" : "wurden")} {successCount} Nutzer erfolgreich **unpermitted**!";

                    await msg.ModifyAsync(endstring);
                }
            }
        );
    }
}