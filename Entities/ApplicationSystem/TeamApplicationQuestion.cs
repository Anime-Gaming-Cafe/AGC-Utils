#region

using AGC_Management.Enums.ApplicationSystem;

#endregion

namespace AGC_Management.Entities.ApplicationSystem;

public class TeamApplicationQuestion
{
    public string QuestionId { get; set; } = "";
    public string PositionId { get; set; } = "";
    public int Version { get; set; }
    public int SortOrder { get; set; }
    public TeamApplicationQuestionType Type { get; set; } = TeamApplicationQuestionType.ShortText;
    public string Text { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Required { get; set; } = true;
    public int MinLength { get; set; }
    public int MaxLength { get; set; }
    public long MinValue { get; set; }
    public long MaxValue { get; set; }
    public List<string> Options { get; set; } = [];
    public int MinSelections { get; set; }
    public int MaxSelections { get; set; }

    public bool IsChoice => Type is TeamApplicationQuestionType.SingleChoice
        or TeamApplicationQuestionType.MultipleChoice;

    public bool SupportsLength => Type is TeamApplicationQuestionType.ShortText
        or TeamApplicationQuestionType.LongText;

    public bool SupportsRange => Type is TeamApplicationQuestionType.Number;

    public bool SupportsSelectionLimits => Type is TeamApplicationQuestionType.MultipleChoice;
}
