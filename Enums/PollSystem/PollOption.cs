namespace AGC_Management.Enums.PollSystem;

public class PollOption
{
    public string Id { get; set; }
    public int Index { get; set; }
    public string Name { get; set; }
    public int Votes { get; set; } = 0;
    public List<ulong> Voters { get; set; } = [];
    public string PollId { get; set; } // Foreign Key
}