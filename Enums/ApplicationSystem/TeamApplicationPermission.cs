namespace AGC_Management.Enums.ApplicationSystem;

public enum TeamApplicationPermission
{
    /// <summary>Satisfies every other permission in this enum. Never covers administrator-only actions.</summary>
    FullAccess,
    ViewApplications,
    MarkInternal,
    Notes,
    Decide,
    ManagePhases,
    ManageQuestions,
    GrantReapply,
    ViewModerationHistory
}
