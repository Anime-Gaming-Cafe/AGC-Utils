#region

using AGC_Management.Enums.Autoposts;

#endregion

namespace AGC_Management.Entities.Autoposts;

public class AutopostCondition
{
    public string ConditionId { get; set; } = "";
    public string AutopostId { get; set; } = "";
    public AutopostConditionType Type { get; set; }
    public bool Negate { get; set; }
    public AutopostConditionParams Params { get; set; } = new();
    public long CreatedAt { get; set; }
}

/// <summary>
///     One flat bag for every condition type, stored as JSONB. Each type reads only its own fields, which
///     keeps the dashboard binding trivial and a new type a matter of adding fields here.
/// </summary>
public class AutopostConditionParams
{
    public string PositionId { get; set; } = "";

    public string From { get; set; } = "10:00";
    public string To { get; set; } = "22:00";
    public List<int> Weekdays { get; set; } = [1, 2, 3, 4, 5, 6, 7];

    public long DateFrom { get; set; }
    public long DateTo { get; set; }

    public int MinMessages { get; set; } = 10;
    public int WindowMinutes { get; set; } = 30;

    public int Minutes { get; set; } = 60;
}

public class AutopostQueueEntry
{
    public string QueueId { get; set; } = "";
    public string AutopostId { get; set; } = "";
    public int StepIndex { get; set; }
    public long DueAt { get; set; }
    public bool CheckFilters { get; set; }
}
