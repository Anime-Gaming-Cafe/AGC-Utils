namespace AGC_Management.Enums.Web;

public static class AccessLevels
{
    public static string[] AtLeast(AccessLevel level) =>
        Enum.GetValues<AccessLevel>()
            .Where(l => l >= level && l is not (AccessLevel.NichtImServer or AccessLevel.Blacklisted))
            .Select(l => l.ToString())
            .ToArray();
}
