namespace AGC_Management.Entities.ApplicationSystem;

/// <summary>Message counts and voice minutes over the three windows shown on the detail page.</summary>
public class TeamApplicationActivity
{
    public long Messages7 { get; set; }
    public long Messages30 { get; set; }
    public long Messages90 { get; set; }
    public long VoiceMinutes7 { get; set; }
    public long VoiceMinutes30 { get; set; }
    public long VoiceMinutes90 { get; set; }
}
