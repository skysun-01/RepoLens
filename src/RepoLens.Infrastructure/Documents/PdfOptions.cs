namespace RepoLens.Infrastructure.Documents;

public sealed class PdfOptions
{
    public const string SectionName = "Pdf";

    /// <summary>
    /// QuestPDF license: Community is free for organizations under USD 1M annual revenue, individuals and
    /// non-profits; otherwise use Professional or Enterprise. See https://www.questpdf.com/license/.
    /// </summary>
    public string QuestPdfLicense { get; set; } = "Community";
}
