#region

using AGC_Management.Enums.ApplicationSystem;

#endregion

namespace AGC_Management.Entities.ApplicationSystem;

public class TeamApplicationTemplate
{
    public string TemplateId { get; set; } = "";

    /// <summary>Null means the template is available for every position.</summary>
    public string? PositionId { get; set; }

    public TeamApplicationTemplateKind Kind { get; set; } = TeamApplicationTemplateKind.Accept;
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}
