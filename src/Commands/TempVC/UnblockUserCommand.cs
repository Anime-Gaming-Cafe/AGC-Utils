﻿#region

using AGC_Management.Attributes;
using AGC_Management.Utils;
using AGC_Management.Utils.TempVoice;
using DisCatSharp.CommandsNext;
using DisCatSharp.CommandsNext.Attributes;
using DisCatSharp.Entities;
using DisCatSharp.Enums;
using Microsoft.Extensions.Logging;

#endregion

namespace AGC_Management.Commands.TempVC;

public sealed class UnblockUserCommand : TempVoiceHelper
{
    [Command("block")]
    [RequireDatabase]
    [Aliases("vcban", "multiblock")]
    public async Task VoiceBlock(CommandContext ctx, [RemainingText] string users)
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
                    var blockedlist = new List<ulong>();
                    List<ulong> ids = new();
                    ids = Converter.ExtractUserIDsFromString(users);
                    var staffrole = ctx.Guild.GetRole(GlobalProperties.StaffRoleId);
                    var msg = await ctx.RespondAsync(
                        $"<a:loading_agc:1084157150747697203> **Lade...** Versuche {ids.Count} Nutzer zu blockieren...");
                    var overwrites = userChannel.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();

                    foreach (ulong id in ids)
                    {
                        try
                        {
                            var user = await ctx.Guild.GetMemberAsync(id);

                            if (user.Roles.Contains(staffrole))
                            {
                                continue;
                            }

                            var channelowner = await GetChannelOwnerID(userChannel);
                            if (channelowner == (long)user.Id)
                            {
                                continue;
                            }

                            List<ulong> mods = await RetrieveChannelMods(userChannel);
                            if (id == ctx.User.Id || mods.Contains(id))
                            {
                                continue;
                            }

                            try
                            {
                                var currentmods = await RetrieveChannelMods(userChannel);

                                currentmods.Remove(id);
                                await UpdateChannelMods(userChannel, currentmods);
                            }
                            catch (Exception)
                            {
                            }

                            overwrites = overwrites.Merge(user, Permissions.None, Permissions.UseVoice);

                            blockedlist.Add(user.Id);
                        }
                        catch (Exception ex)
                        {
                            ctx.Client.Logger.LogCritical(ex.Message);
                            ctx.Client.Logger.LogCritical(ex.StackTrace);
                        }
                    }

                    try
                    {
                        await userChannel.ModifyAsync(x => x.PermissionOverwrites = overwrites);
                    }
                    catch (Exception e)
                    {
                        ctx.Client.Logger.LogCritical(e.Message);
                        ctx.Client.Logger.LogCritical(e.StackTrace);
                    }


                    foreach (ulong id in blockedlist)
                    {
                        try
                        {
                            var user = await ctx.Guild.GetMemberAsync(id);
                            if (user.Roles.Contains(staffrole))
                            {
                                continue;
                            }

                            if (userChannel.Users.Contains(user) && !user.Roles.Contains(staffrole))
                            {
                                await user.DisconnectFromVoiceAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            ctx.Client.Logger.LogCritical(ex.Message);
                            ctx.Client.Logger.LogCritical(ex.StackTrace);
                        }
                    }


                    int successCount = blockedlist.Count;
                    string endstring =
                        $"<:success:1085333481820790944> **Erfolg!** Es {(successCount == 1 ? "wurde" : "wurden")} {successCount} Nutzer erfolgreich **blockiert**!";

                    await msg.ModifyAsync(endstring);
                }
            }
        );
    }
}