#region

using AGC_Management.Enums.ApplicationSystem;

#endregion

namespace AGC_Management.Entities.ApplicationSystem;

public class TeamApplicationAnswer
{
    public string ApplicationId { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public int SortOrder { get; set; }
    public string QuestionTextSnapshot { get; set; } = "";
    public TeamApplicationQuestionType QuestionType { get; set; } = TeamApplicationQuestionType.ShortText;
    public string Answer { get; set; } = "";
    public List<string> AnswerOptions { get; set; } = [];
    public string OtherText { get; set; } = "";

    // Which option OtherText belongs to, snapshotted at submit time - needed to place it next to
    // the right option when rendering a MultipleChoice answer, since AnswerOptions can hold
    // several values and only one of them opens the free-text field.
    public string OtherOptionSnapshot { get; set; } = "";
}
