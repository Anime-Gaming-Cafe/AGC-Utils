#region

using AGC_Management.Services;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Providers;

public abstract class EntbannungAutocompleteProvider(string type) : IAutocompleteProvider
{
    public async Task<IEnumerable<DiscordApplicationCommandAutocompleteChoice>> Provider(AutocompleteContext ctx)
    {
        var search = ctx.FocusedOption?.Value?.ToString() ?? "";
        var completions = await EntbannungService.GetAutocompletionsAsync(type);

        return completions
            .Where(c => string.IsNullOrWhiteSpace(search) || c.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(c => new DiscordApplicationCommandAutocompleteChoice(c[..Math.Min(c.Length, 100)], c));
    }
}

public sealed class SperreAutocompleteProvider() : EntbannungAutocompleteProvider("sperre");

public sealed class RevokeBanAutocompleteProvider() : EntbannungAutocompleteProvider("entbannung");
