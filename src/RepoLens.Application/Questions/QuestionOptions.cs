using System.ComponentModel.DataAnnotations;

namespace RepoLens.Application.Questions;

public sealed class QuestionOptions
{
    public const string SectionName = "Questions";

    [Range(1, 4_000)]
    public int MaxQuestionLength { get; set; } = 2_000;

    /// <summary>Code chunks retrieved per question.</summary>
    [Range(1, 50)]
    public int TopK { get; set; } = 8;

    /// <summary>Earlier messages of the conversation sent as context (user and assistant messages both count).</summary>
    [Range(0, 50)]
    public int HistoryMessages { get; set; } = 6;

    [Range(64, 32_000)]
    public int MaxAnswerTokens { get; set; } = 2_000;
}
