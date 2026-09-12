namespace AGC_Management.Utils;

public static class DashboardPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string ModUp = "ModUp";
    public const string SupportUp = "SupportUp";
    public const string EventManagement = "EventManagement"; // skips Moderator/Supporter/Team, so not a plain level cutoff
    public const string AnyStaff = "AnyStaff";
    public const string AnyMember = "AnyMember";
}
