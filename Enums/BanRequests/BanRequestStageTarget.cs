namespace AGC_Management.Enums.BanRequests;

public enum BanRequestStageTarget
{
    /// <summary>Everyone the availability estimate considers reachable right now, pinged individually.</summary>
    Estimated,

    Role,

    /// <summary>Every team member with ban permission, pinged individually.</summary>
    All
}
