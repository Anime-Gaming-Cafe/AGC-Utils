#region

using AGC_Management.Utils;

#endregion

namespace AGC_Management.Entities;

public class Application
{
    public string BewerbungsId { get; set; } = ToolSet.GenerateCaseID();
    public long UserId { get; set; }
    public string PositionName { get; set; }
    public int Status { get; set; } = 0;
    public long Timestamp { get; set; }
    public string Bewerbungstext { get; set; } = "";
    public List<long> SeenBy { get; set; } = new();
}