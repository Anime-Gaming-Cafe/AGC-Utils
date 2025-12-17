#region

using AGC_Management.Attributes;
using AGC_Management.TempVoice;

#endregion

namespace AGC_Management.Commands.TempVC;

[Group("channelmod")]
[Aliases("cmod")]
public class ChannelModManagement : TempVoiceHelper
{
    [RequireDatabase]
    [Command("add")]
    public async Task ChannelModAdd(CommandContext ctx, [RemainingText] DiscordMember user)
    {
        List<ulong> channelmods = new();
        var dbChannels = await GetChannelIDFromDB(ctx);

        var userChannel = ctx.Member?.VoiceState?.Channel;

        if (userChannel == null || !dbChannels.Contains((long)userChannel?.Id))
        {
            await NoChannel(ctx);
            return;
        }

        if (userChannel != null && dbChannels.Contains((long)userChannel.Id))
        {
            var isMod = await IsChannelMod(userChannel, user);
            if (isMod)
            {
                await ctx.RespondAsync("Dieser User ist bereits Kanalmoderator");
                return;
            }

            var currentmods = await RetrieveChannelMods(userChannel);
            currentmods.Add(user.Id);
            await UpdateChannelMods(userChannel, currentmods);
            await ctx.RespondAsync(
                $"Der User ``{user.UsernameWithDiscriminator}`` ``{user.Id}`` wurde als Kanalmoderator hinzugefügt.");
        }
    }

    [RequireDatabase]
    [Command("reset")]
    public async Task ChannelModReset(CommandContext ctx)
    {
        var dbChannels = await GetChannelIDFromDB(ctx);
        var userChannel = ctx.Member?.VoiceState?.Channel;

        if (userChannel == null || !dbChannels.Contains((long)userChannel?.Id))
        {
            await NoChannel(ctx);
            return;
        }

        if (userChannel != null && dbChannels.Contains((long)userChannel.Id))
        {
            if (!ChannelHasMods(userChannel).Result)
            {
                await ctx.RespondAsync("Dieser Kanal hat keine Kanalmoderatoren.");
                return;
            }

            await ResetChannelMods(userChannel);
            await ctx.RespondAsync("Die Kanalmoderatoren wurden zurückgesetzt.");
        }
    }

    [RequireDatabase]
    [Command("remove")]
    public async Task ChannelModRemove(CommandContext ctx, [RemainingText] DiscordMember user)
    {
        List<ulong> channelmods = new();
        var dbChannels = await GetChannelIDFromDB(ctx);

        var userChannel = ctx.Member?.VoiceState?.Channel;

        if (userChannel == null || !dbChannels.Contains((long)userChannel?.Id))
        {
            await NoChannel(ctx);
            return;
        }

        if (userChannel != null && dbChannels.Contains((long)userChannel.Id))
        {
            var isMod = await IsChannelMod(userChannel, user);
            if (!isMod)
            {
                await ctx.RespondAsync("Dieser User ist kein Kanalmoderator");
                return;
            }

            var currentmods = await RetrieveChannelMods(userChannel);
            currentmods.Remove(user.Id);
            await UpdateChannelMods(userChannel, currentmods);
            await ctx.RespondAsync(
                $"Der User ``{user.UsernameWithDiscriminator}`` ``{user.Id}`` wurde als Kanalmoderator entfernt.");
        }
    }

    [RequireDatabase]
    [Command("list")]
    public async Task ChannelModList(CommandContext ctx)
    {
        List<ulong> channelmods = new();
        var dbChannels = await GetChannelIDFromDB(ctx);

        var userChannel = ctx.Member?.VoiceState?.Channel;

        if (userChannel == null || !dbChannels.Contains((long)userChannel?.Id))
        {
            await NoChannel(ctx);
            return;
        }

        if (userChannel != null && dbChannels.Contains((long)userChannel.Id))
        {
            if (!ChannelHasMods(userChannel).Result)
            {
                await ctx.RespondAsync("Dieser Kanal hat keine Kanalmoderatoren.");
                return;
            }

            var currentmods = await RetrieveChannelMods(userChannel);
            var modlist = string.Empty;
            foreach (var mod in currentmods)
            {
                var member = await ctx.Guild.GetMemberAsync(mod);
                modlist += $"{member.UsernameWithDiscriminator} ``({member.Id})``\n";
            }

            var emb = new DiscordEmbedBuilder().WithDescription(modlist).WithColor(BotConfig.GetEmbedColor())
                .WithTitle("Kanalmoderatoren")
                .WithFooter($"{ctx.User.UsernameWithDiscriminator} | {userChannel.Name}");
            await ctx.RespondAsync(emb);
        }
    }
}