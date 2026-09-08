#region

using AGC_Management.Enums.ExtraPermissions;

#endregion

namespace AGC_Management.Entities.ExtraPermissions;

public class ExtraPermissionMemberState
{
    public ulong UserId { get; set; }
    public string PermName { get; set; } = "";
    public ExtraPermissionState State { get; set; } = ExtraPermissionState.Auto;
    public long ExpiresAt { get; set; }
    public ulong ActorId { get; set; }
    public string Reason { get; set; } = "";
    public long UpdatedAt { get; set; }
    public bool TriggerFired { get; set; }
    public long TriggerFiredAt { get; set; }

    public bool IsExpired => ExpiresAt > 0 && ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public ExtraPermissionState EffectiveState => IsExpired ? ExtraPermissionState.Auto : State;
}
