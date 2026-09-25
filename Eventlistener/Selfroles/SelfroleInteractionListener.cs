#region

using AGC_Management.Components;
using AGC_Management.Entities.Selfroles;
using AGC_Management.Services;

#endregion

namespace AGC_Management.Eventlistener.Selfroles;

/// <summary>
///     Answers the panel button and everything in the menu behind it. Matching happens on the custom id
///     prefix, not on the channel the way the Python original did it, so any other component in the same
///     channel is left alone instead of running into the handler.
/// </summary>
[EventHandler]
public sealed class SelfroleInteractionListener : BaseCommandModule
{
    [Event]
    public Task ComponentInteractionCreated(DiscordClient client, ComponentInteractionCreateEventArgs args)
    {
        var customId = args.Interaction.Data.CustomId;
        if (string.IsNullOrEmpty(customId) || !customId.StartsWith(SelfroleComponents.Prefix, StringComparison.Ordinal))
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                await HandleAsync(args, customId);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e, "Selfroles: Interaktion {CustomId} fehlgeschlagen", customId);
            }
        });

        return Task.CompletedTask;
    }

    private static async Task HandleAsync(ComponentInteractionCreateEventArgs args, string customId)
    {
        var guild = CurrentApplication.TargetGuild;
        if (guild is null || args.Guild?.Id != guild.Id) return;

        var panel = await SelfroleService.GetPanelAsync();
        if (!panel.Enabled)
        {
            await Reject(args, "Das Selfrole-System ist aktuell deaktiviert.");
            return;
        }

        var member = await ResolveMember(guild, args.User.Id);
        if (member is null) return;

        var (categoryId, argument) = Split(customId);
        var category = panel.Categories.FirstOrDefault(c => c.Id == categoryId && c.Enabled);
        if (category is null)
        {
            await Reject(args, "Diese Kategorie gibt es nicht mehr.");
            return;
        }

        if (customId.StartsWith(SelfroleComponents.OpenPrefix, StringComparison.Ordinal))
        {
            await OpenMenu(args, category, SelfroleService.HeldOptionIds(member, category));
            return;
        }

        if (customId.StartsWith(SelfroleComponents.AutoPrefix, StringComparison.Ordinal))
        {
            var optedOut = await SelfroleService.IsOptedOutAsync(member.Id);
            await SelfroleService.SetOptOutAsync(member.Id, !optedOut);
            await UpdateMenu(args, category, SelfroleService.HeldOptionIds(member, category),
                optedOut ? "Autozuweisung aktiviert." : "Autozuweisung deaktiviert.");
            return;
        }

        SelfroleChange change;
        if (customId.StartsWith(SelfroleComponents.ClearPrefix, StringComparison.Ordinal))
        {
            change = await SelfroleService.ClearAsync(member, category);
        }
        else if (customId.StartsWith(SelfroleComponents.TogglePrefix, StringComparison.Ordinal))
        {
            var option = category.ActiveOptions.FirstOrDefault(o => o.Id == argument);
            if (option is null)
            {
                await Reject(args, "Diese Option gibt es nicht mehr.");
                return;
            }

            change = await SelfroleService.ApplyToggleAsync(member, category, option);
        }
        else if (customId.StartsWith(SelfroleComponents.SelectPrefix, StringComparison.Ordinal))
        {
            var scope = ResolveChunk(category, argument);
            var selected = args.Interaction.Data.Values ?? [];
            change = await SelfroleService.ApplySelectionAsync(member, category, scope, selected);
        }
        else
        {
            return;
        }

        // The cached member still carries the roles from before the change, so the menu is rebuilt from
        // what the change itself reports. A rejected change left the roles alone and reads them back.
        var held = change.Held ?? SelfroleService.HeldOptionIds(member, category);
        await UpdateMenu(args, category, held, SelfroleComponents.DescribeChange(change));
    }

    /// <summary>The cache holds every member, so this only reaches Discord in the rare miss.</summary>
    private static async Task<DiscordMember?> ResolveMember(DiscordGuild guild, ulong userId)
    {
        if (guild.Members.TryGetValue(userId, out var cached)) return cached;

        try
        {
            return await guild.GetMemberAsync(userId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The options one rendered select showed, which is the scope its selection replaces.</summary>
    private static List<SelfroleOption> ResolveChunk(SelfroleCategory category, string argument)
    {
        if (!int.TryParse(argument, out var chunk) || chunk < 0) chunk = 0;
        return [.. category.ActiveOptions.Skip(chunk * category.ChunkSize).Take(category.ChunkSize)];
    }

    private static (string CategoryId, string Argument) Split(string customId)
    {
        var parts = customId[SelfroleComponents.Prefix.Length..].Split(':');
        return parts.Length < 2 ? ("", "") : (parts[1], parts.Length > 2 ? parts[2] : "");
    }

    private static async Task<bool?> ResolveAutoAssignState(SelfroleCategory category, ulong userId)
    {
        if (!category.AutoAssign || !await SelfroleService.AutoDetectEnabledAsync()) return null;
        return !await SelfroleService.IsOptedOutAsync(userId);
    }

    private static async Task OpenMenu(ComponentInteractionCreateEventArgs args, SelfroleCategory category,
        IReadOnlySet<string> held)
    {
        var autoAssign = await ResolveAutoAssignState(category, args.User.Id);
        var container = SelfroleComponents.BuildCategoryMenu(category, held, null, autoAssign);
        await args.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithV2Components().AddComponents([container]).AsEphemeral());
    }

    private static async Task UpdateMenu(ComponentInteractionCreateEventArgs args, SelfroleCategory category,
        IReadOnlySet<string> held, string notice)
    {
        var autoAssign = await ResolveAutoAssignState(category, args.User.Id);
        var container = SelfroleComponents.BuildCategoryMenu(category, held, notice, autoAssign);
        await args.Interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage,
            new DiscordInteractionResponseBuilder().WithV2Components().AddComponents([container]));
    }

    private static Task Reject(ComponentInteractionCreateEventArgs args, string message)
    {
        var container = new DiscordContainerComponent([new DiscordTextDisplayComponent(message)],
            accentColor: BotConfig.GetEmbedColor());

        return args.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithV2Components().AddComponents([container]).AsEphemeral());
    }
}
