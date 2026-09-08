#region

using AGC_Management.Services;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;

#endregion

namespace AGC_Management.Providers;

public class ExtraPermissionConditionAutocompleteProvider : IAutocompleteProvider
{
    public async Task<IEnumerable<DiscordApplicationCommandAutocompleteChoice>> Provider(AutocompleteContext ctx)
    {
        var permName = ctx.Options
            .FirstOrDefault(o => o.Name == "permission")?.Value?.ToString();

        if (string.IsNullOrWhiteSpace(permName)) return [];

        var conditions = await ExtraPermissionService.GetConditionsAsync(permName);

        return conditions
            .Take(25)
            .Select(c =>
            {
                var label = $"({c.GroupId}) {ExtraPermissionFormatter.DescribeCondition(c)}";
                if (label.Length > 100) label = label[..97] + "...";

                return new DiscordApplicationCommandAutocompleteChoice(label, c.ConditionId);
            });
    }
}
