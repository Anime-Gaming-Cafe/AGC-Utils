using AGC_Management.Entities.ApplicationSystem;

namespace AGC_Management.Pages.SharedPages;

// Shared between the real application form and the admin preview, so both render off the same model.
public sealed class QuestionAnswerVm
{
    public TeamApplicationQuestion Question { get; init; } = new();
    public string Text { get; set; } = "";
    public List<string> Selected { get; set; } = [];
    public string? Error { get; set; }
}
