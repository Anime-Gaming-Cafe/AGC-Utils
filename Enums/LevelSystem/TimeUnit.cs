#region

using DisCatSharp.ApplicationCommands.Attributes;

#endregion

namespace AGC_Management.Enums.LevelSystem;

public enum TimeUnit
{
    [ChoiceName("Minuten")] Minutes,
    [ChoiceName("Stunden")] Hours,
    [ChoiceName("Tage")] Days,
    [ChoiceName("Wochen")] Weeks,
    [ChoiceName("Monate")] Months
}