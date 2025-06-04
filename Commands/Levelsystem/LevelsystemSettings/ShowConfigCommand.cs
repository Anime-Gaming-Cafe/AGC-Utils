#region

using System.Text;
using AGC_Management.Entities;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Commands.Levelsystem;

public partial class LevelSystemSettings
{
    [ApplicationCommandRequirePermissions(Permissions.Administrator)]
    [SlashCommand("showconfig", "Zeigt die aktuelle Konfiguration des Levelsystems an",
        (long)Permissions.Administrator)]
    public static async Task SetupLevelcommand(InteractionContext ctx)
    {
        var msgbuilder = await GetSetupEmbedAndComponents();
        var interactionsresponsebuilder = new DiscordInteractionResponseBuilder().AddEmbeds(msgbuilder.Embeds);
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, interactionsresponsebuilder);
    }


    public static async Task<DiscordMessageBuilder> GetSetupEmbedAndComponents()
    {
        var levelupmessage = await LevelUtils.GetLevelUpMessage();
        var leveluprewardmessage = await LevelUtils.GetLevelUpRewardMessage();
        var isLevelUpMessageEnabled = await LevelUtils.IsLevelUpMessageEnabled();
        var levelupchannelid = await LevelUtils.GetLevelUpChannelId();
        var levelupchannel = CurrentApplication.TargetGuild?.GetChannel(levelupchannelid);
        var blockedchannels = await LevelUtils.BlockedChannels();
        var blockedroles = await LevelUtils.BlockedRoles();
        var rewards = await LevelUtils.GetLevelRewards();
        var multiplicatorOverrides = await LevelUtils.GetMultiplicatorOverrides();
        var levelmulti_vc = await LevelUtils.GetLevelMultiplier(XpRewardType.Voice);
        var levelmulti_msg = await LevelUtils.GetLevelMultiplier(XpRewardType.Message);

        var isLevelingEnabledForVoice = await LevelUtils.IsLevelingEnabled(XpRewardType.Voice);
        var isLevelingEnabledForMessage = await LevelUtils.IsLevelingEnabled(XpRewardType.Message);


        // local methode
        string GetBlockedChannelsString()
        {
            var sb = new StringBuilder();
            foreach (var blockedchannel in blockedchannels)
            {
                var channel = CurrentApplication.TargetGuild?.GetChannel(blockedchannel);
                if (channel != null)
                    sb.AppendLine($"- {channel.Mention}");
                else
                    sb.AppendLine($"- Kanal gelöscht ``{blockedchannel}``");
            }

            return sb.ToString();
        }

        // local methode
        string GetBlockedRolesString()
        {
            var sb = new StringBuilder();
            foreach (var blockedrole in blockedroles)
            {
                var role = CurrentApplication.TargetGuild?.GetRole(blockedrole);
                if (role != null)
                    sb.AppendLine($"- {role.Mention}");
                else
                    sb.AppendLine($"- Rolle gelöscht ``{blockedrole}``");
            }

            return sb.ToString();
        }

        // local methode
        string GetLevelUpRolesStringSorted()
        {
            var sb = new StringBuilder();
            foreach (var reward in rewards.OrderBy(x => x.Level))
            {
                var role = CurrentApplication.TargetGuild?.GetRole(reward.RoleId);
                if (role != null)
                    sb.AppendLine($"- Level ``{reward.Level}``: {role.Mention}");
                else
                    sb.AppendLine($"- Rolle gelöscht ``{reward.RoleId}`` - Level {reward.Level}");
            }

            return sb.ToString();
        }

        // local methode
        string GetOverrideRolesString()
        {
            var sb = new StringBuilder();
            foreach (var overrideRole in multiplicatorOverrides)
            {
                var role = CurrentApplication.TargetGuild?.GetRole(overrideRole.RoleId);
                if (role != null)
                    sb.AppendLine($"- {role.Mention}: {overrideRole.Multiplicator}x");
                else
                    sb.AppendLine($"- Rolle gelöscht ``{overrideRole.RoleId}`` - {overrideRole.Multiplicator}x");
            }

            return sb.ToString();
        }

        var embedDescString = new StringBuilder();
        embedDescString.AppendLine("__**Levelup Nachricht**__");
        if (isLevelUpMessageEnabled)
        {
            embedDescString.AppendLine("\u2705 - Wenn kein Reward vergeben wird:");
            embedDescString.AppendLine($"```{levelupmessage}```");
            embedDescString.AppendLine("Wenn ein Reward vergeben wird:");
            embedDescString.AppendLine($"```{leveluprewardmessage}```");
        }
        else
        {
            embedDescString.AppendLine("\u274c Deaktiviert");
        }

        embedDescString.AppendLine();

        embedDescString.AppendLine("__**Level Multiplikator**__");
        embedDescString.AppendLine(
            $"{MessageFormatter.BoolToEmoji(isLevelingEnabledForVoice)} - Voice: ``{levelmulti_vc}x``");
        embedDescString.AppendLine(
            $"{MessageFormatter.BoolToEmoji(isLevelingEnabledForMessage)} - Message: ``{levelmulti_msg}x``");
        embedDescString.AppendLine();

        embedDescString.AppendLine("__**Kanal für Levelup Nachrichten**__");
        if (levelupchannel != null)
            embedDescString.AppendLine($"✅ - {levelupchannel.Mention}");
        else if (levelupchannelid != 0)
            embedDescString.AppendLine("\u274c - Kanal gelöscht ``levelupchannelid``");
        else if (levelupchannelid == 0) embedDescString.AppendLine("\u274c - Kein Kanal ausgewählt");

        embedDescString.AppendLine();
        embedDescString.AppendLine("__**Ausgeschlossene Kanäle**__");
        if (blockedchannels.Count > 0)
        {
            embedDescString.AppendLine(
                $"✅ - **In diesen Kanälen wird kein XP vergeben ``{blockedchannels.Count} Kanäle``**");
            embedDescString.AppendLine(GetBlockedChannelsString());
        }
        else
        {
            embedDescString.AppendLine("\u274c - Keine Kanäle ausgeschlossen");
        }

        embedDescString.AppendLine("__**Ausgeschlossene Rollen**__");
        if (blockedroles.Count > 0)
        {
            embedDescString.AppendLine(
                $"✅ - **User mit diesen Rollen erhalten kein XP ``{blockedroles.Count} Rollen``**");
            embedDescString.AppendLine(GetBlockedRolesString());
        }
        else
        {
            embedDescString.AppendLine("\u274c - Keine Rollen ausgeschlossen");
        }

        embedDescString.AppendLine();
        embedDescString.AppendLine("__**Level Rollenbelohnung**__");
        if (rewards.Count > 0)
        {
            embedDescString.AppendLine($"✅ - **User erhalten für diese Level eine Rolle ``{rewards.Count} Rollen``**");
            embedDescString.AppendLine(GetLevelUpRolesStringSorted());
        }
        else
        {
            embedDescString.AppendLine("\u274c - Keine Rollenbelohnungen");
        }

        embedDescString.AppendLine();

        embedDescString.AppendLine("__**Level Multiplicatoroverriderollen**__");
        if (multiplicatorOverrides.Count > 0)
        {
            embedDescString.AppendLine(
                $"✅ - **User erhalten für diese Rollen einen XP-Multiplikator ``{multiplicatorOverrides.Count} Rollen``**");
            embedDescString.AppendLine(GetOverrideRolesString());
        }
        else
        {
            embedDescString.AppendLine("\u274c - Keine Multiplicatoroverriderollen");
        }

        embedDescString.AppendLine();


        var embed = new DiscordEmbedBuilder()
            .WithTitle("Levelsystem Konfiguration")
            .WithDescription(embedDescString.ToString())
            .WithColor(DiscordColor.Blurple);

        var messageBuilder = new DiscordMessageBuilder().AddEmbed(embed.Build());

        return messageBuilder;
    }
}