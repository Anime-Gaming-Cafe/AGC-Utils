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

    // Empty means unconditional. Points at another question in the same set; the field only
    // shows once that question's answer equals ConditionValue (or contains it, for MultipleChoice).
    public string ConditionQuestionId { get; set; } = "";
    public string ConditionValue { get; set; } = "";

    // Only meaningful for Type == Number. Slider/Dropdown render the same numeric answer as a
    // different widget instead of a free-text box; both need MinValue/MaxValue to be a real range.
    public NumberDisplay NumberDisplay { get; set; } = NumberDisplay.Text;
    public int NumberStep { get; set; } = 1;

    public bool IsChoice => Type is TeamApplicationQuestionType.SingleChoice
        or TeamApplicationQuestionType.MultipleChoice
        or TeamApplicationQuestionType.Dropdown;

    public bool SupportsLength => Type is TeamApplicationQuestionType.ShortText
        or TeamApplicationQuestionType.LongText;

    public bool SupportsRange => Type is TeamApplicationQuestionType.Number;

    public bool SupportsSelectionLimits => Type is TeamApplicationQuestionType.MultipleChoice;
}
