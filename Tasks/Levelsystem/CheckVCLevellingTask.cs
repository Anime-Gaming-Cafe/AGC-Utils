#region

using AGC_Management.Entities;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Tasks;

public static class CheckVCLevellingTask
{
    public static async Task Run()
    {
        await StartCheckVCLevelling();
    }

    private static async Task StartCheckVCLevelling()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        while (true)
        {
            if (CurrentApplication.TargetGuild == null)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                continue;
            }

            try
            {
                var users = new Dictionary<ulong, ulong>();
                var guild = CurrentApplication.TargetGuild;
                foreach (var channel in guild.Channels.Values)
                    if (channel.Type == ChannelType.Voice || channel.Type == ChannelType.Stage)
                        foreach (var member in channel.Users)
                            if (!users.ContainsKey(member.Id))
                            {
                                if (member.IsBot) continue;


                                if (member.VoiceState?.IsSelfMuted == true ||
                                    member.VoiceState?.IsSelfDeafened == true ||
                                    member.VoiceState?.IsServerMuted == true ||
                                    member.VoiceState?.IsServerDeafened == true)
                                    continue;

                                var count = 0;
                                foreach (var user in channel.Users)
                                {
                                    if (user.IsBot) continue;

                                    if (user.VoiceState?.IsSelfMuted == true ||
                                        user.VoiceState?.IsSelfDeafened == true ||
                                        user.VoiceState?.IsServerMuted == true ||
                                        user.VoiceState?.IsServerDeafened == true)
                                        continue;

                                    count++;
                                }

                                if (count < 2) continue;


                                if (await LevelUtils.IsChannelBlocked(channel.Id)) continue;

                                users.Add(member.Id, channel.Id);
                            }

                var memberIds = users.Keys.ToList();
                var members = new List<DiscordMember>();
                foreach (var memberId in memberIds)
                {
                    DiscordMember member = null;
                    try
                    {
                        member = await guild.GetMemberAsync(memberId);
                    }
                    catch (Exception e)
                    {
                        continue;
                    }

                    if (member != null) members.Add(member);
                }

                foreach (var member in members)
                    await LevelUtils.GiveXP(member, LevelUtils.GetBaseXp(XpRewardType.Voice), XpRewardType.Voice);

                users.Clear();
                memberIds.Clear();
                members.Clear();
            }
            catch (Exception e)
            {
                await ErrorReporting.SendErrorToDev(CurrentApplication.DiscordClient, null, e);
            }

            await Task.Delay(TimeSpan.FromSeconds(61));
        }
    }
}