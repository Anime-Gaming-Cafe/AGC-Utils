namespace AGC_Management.Services;

public static class AutoQuoteService
{
    public const string Section = "AutoQuote";

    private const string ExcludedSourceKey = "ExcludedSourceIds";
    private const string ExcludedListenKey = "ExcludedListenIds";

    public static Task<bool> GetEnabledAsync()
    {
        return RuntimeSettings.GetBoolAsync(Section, "Enabled", false);
    }

    public static Task SetEnabledAsync(bool enabled)
    {
        return RuntimeSettings.SetAsync(Section, "Enabled", enabled.ToString());
    }

    public static async Task<IReadOnlyList<ulong>> GetExcludedSourcesAsync()
    {
        return ParseIds(await RuntimeSettings.GetAsync(Section, ExcludedSourceKey));
    }

    public static async Task<IReadOnlyList<ulong>> GetExcludedListenAsync()
    {
        return ParseIds(await RuntimeSettings.GetAsync(Section, ExcludedListenKey));
    }

    public static Task SetExcludedSourcesAsync(IEnumerable<ulong> ids)
    {
        return RuntimeSettings.SetAsync(Section, ExcludedSourceKey, string.Join(",", ids.Distinct()));
    }

    public static Task SetExcludedListenAsync(IEnumerable<ulong> ids)
    {
        return RuntimeSettings.SetAsync(Section, ExcludedListenKey, string.Join(",", ids.Distinct()));
    }

    public static bool IsExcluded(DiscordChannel channel, IReadOnlyList<ulong> ids)
    {
        if (ids.Count == 0) return false;

        DiscordChannel? current = channel;
        while (current is not null)
        {
            if (ids.Contains(current.Id)) return true;
            current = current.Parent;
        }

        return false;
    }

    private static IReadOnlyList<ulong> ParseIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        return
        [
            .. raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => ulong.TryParse(part, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
        ];
    }
}
