namespace AGC_Management.Entities.ApplicationSystem;

public class TeamApplicationNote
{
    public string NoteId { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public ulong AuthorId { get; set; }
    public string Text { get; set; } = "";
    public long CreatedAt { get; set; }
}
