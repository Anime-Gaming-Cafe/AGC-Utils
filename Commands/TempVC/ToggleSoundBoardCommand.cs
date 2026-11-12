namespace AGC_Management.Commands.TempVC;

public sealed class ToggleSoundBoardCommand : BaseCommandModule
{
    private const ulong TempVcCategoryId = 1484496665909006427;
    private const Permissions SoundboardPermissions = Permissions.UseSoundboard | Permissions.UseExternalSounds;

    [Command("togglesoundboard")]
    [Aliases("vcsoundboard")]
    public async Task ToggleVcSoundboard(CommandContext ctx)
    {
        var channel = ctx.Member?.VoiceState?.Channel;
        if (channel is null ||channel.ParentId != TempVcCategoryId)
        {
            await ctx.RespondAsync(
                "<:attention:1085333468688433232> **Fehler!** Du bist in keinem temporären Voice-Channel.");
            return;
        }

        if (GetOwnerId(channel) != ctx.User.Id)
        {
            await ctx.RespondAsync(
                "<:attention:1085333468688433232> **Fehler!** Nur der Besitzer des Channels kann das Soundboard umschalten.");
            return;
        }

        var msg = await ctx.RespondAsync("Status des Soundboards wird geändert");
        var enable = !GetSoundboardState(channel);
        await SetSoundboardState(channel, enable);
        var state = enable ? "aktiviert" : "deaktiviert";
        await msg.ModifyAsync($"<:success:1085333481820790944> **Erfolg!** Das Soundboard ist nun **{state}**!");
    }

    // horizon-bot grants the owner Speak via a member overwrite; trusted users and former owners only get View+Connect.
    private static ulong? GetOwnerId(DiscordChannel channel)
    {
        var candidates = channel.PermissionOverwrites
            .Where(o => o.Type == OverwriteType.Member)
            .Where(o => o.CheckPermission(Permissions.Speak) == PermissionLevel.Allowed)
            .Where(o => !channel.Guild.Members.TryGetValue(o.Id, out var m) || !m.IsBot)
            .Select(o => o.Id)
            .ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static bool GetSoundboardState(DiscordChannel channel)
    {
        var overwrite = channel.PermissionOverwrites.FirstOrDefault(o => o.Id == channel.Guild.EveryoneRole.Id);
        return overwrite?.CheckPermission(Permissions.UseSoundboard) == PermissionLevel.Allowed;
    }

    private static async Task SetSoundboardState(DiscordChannel channel, bool state)
    {
        var overwrites = channel.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();
        var merged = state
            ? overwrites.Merge(channel.Guild.EveryoneRole, SoundboardPermissions, Permissions.None)
            : overwrites.Merge(channel.Guild.EveryoneRole, Permissions.None, SoundboardPermissions);
        await channel.ModifyAsync(x => x.PermissionOverwrites = merged);
    }
}
