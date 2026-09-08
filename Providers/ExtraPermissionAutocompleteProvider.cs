#region

using AGC_Management.Services;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Providers;

public class ExtraPermissionAutocompleteProvider : IAutocompleteProvider
{
    public async Task<IEnumerable<DiscordApplicationCommandAutocompleteChoice>> Provider(AutocompleteContext ctx)
    {
        var search = ctx.FocusedOption?.Value?.ToString() ?? "";
        var permissions = await ExtraPermissionService.GetPermissionsAsync();

        return permissions
            .Where(p => string.IsNullOrWhiteSpace(search) ||
                        p.PermName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        p.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(p => new DiscordApplicationCommandAutocompleteChoice($"{p.DisplayName} ({p.PermName})", p.PermName));
    }
}
