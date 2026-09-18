namespace AGC_Management.Enums.ActivityRoles;

public enum ActivityRoleWindowType
{
    Rolling,
    CalendarDaily,
    CalendarWeekly,
    CalendarMonthly,
    AllTime,

    /// <summary>
    ///     Reserved for a future fixed start/end date window (Statbot's "Day Range"). Not evaluated yet -
    ///     ResolveWindow has no case for it, so a rule saved with this value is inert until that lands.
    /// </summary>
    FixedRange
}
