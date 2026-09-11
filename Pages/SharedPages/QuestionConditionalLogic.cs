using AGC_Management.Enums.ApplicationSystem;

namespace AGC_Management.Pages.SharedPages;

// Shared by QuestionAnswerForm (rendering/validation) and TeamApplyForm (submit payload), so a
// question's visibility rule is evaluated exactly the same way everywhere it matters.
public static class QuestionConditionalLogic
{
    public static bool IsVisible(QuestionAnswerVm answer, IReadOnlyList<QuestionAnswerVm> allAnswers)
    {
        var question = answer.Question;
        if (string.IsNullOrEmpty(question.ConditionQuestionId)) return true;

        var trigger = allAnswers.FirstOrDefault(a => a.Question.QuestionId == question.ConditionQuestionId);
        if (trigger == null) return true; // trigger question gone - fail open rather than hide silently

        return trigger.Question.Type == TeamApplicationQuestionType.MultipleChoice
            ? trigger.Selected.Contains(question.ConditionValue)
            : trigger.Text == question.ConditionValue;
    }
}
