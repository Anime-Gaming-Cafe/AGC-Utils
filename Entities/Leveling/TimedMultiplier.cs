using AGC_Management.Entities;

namespace AGC_Management.Entities.Leveling;

public class TimedMultiplier
{
    public ulong GuildId { get; set; }
    public XpRewardType Type { get; set; }
    public float Multiplier { get; set; }
    public long ExpiryTimestamp { get; set; }
    public float ResetValue { get; set; }
}