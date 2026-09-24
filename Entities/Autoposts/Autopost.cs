#region

using AGC_Management.Enums.Autoposts;

#endregion

namespace AGC_Management.Entities.Autoposts;

public class Autopost
{
    public string AutopostId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public ulong ChannelId { get; set; }

    public AutopostTriggerType TriggerType { get; set; } = AutopostTriggerType.Messages;
    public int Threshold { get; set; } = 100;

    /// <summary>Channels or categories whose messages count. Empty counts the target channel only.</summary>
    public long[] CountScopeIds { get; set; } = [];

    public bool SubtractLeaves { get; set; } = true;
    public int IntervalMinutes { get; set; } = 60;
    public string ParentAutopostId { get; set; } = "";
    public int ParentDelaySeconds { get; set; }

    public bool DeletePrevious { get; set; }
    public AutopostRotationMode RotationMode { get; set; } = AutopostRotationMode.Sequential;

    public List<AutopostStep> Steps { get; set; } = [];

    public long CounterSince { get; set; }
    public int JoinCount { get; set; }
    public long LastPostedAt { get; set; }
    public long[] LastMessageIds { get; set; } = [];
    public int[] RotationState { get; set; } = [];

    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }

    public List<AutopostCondition> Conditions { get; set; } = [];
}
