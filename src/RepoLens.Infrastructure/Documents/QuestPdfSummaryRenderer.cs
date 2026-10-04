using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RepoLens.Application.Common.Abstractions.Documents;
using RepoLens.Domain.Summaries;

namespace RepoLens.Infrastructure.Documents;

/// <summary>
/// Lays out a <see cref="RepoSummary"/> as an A4 onboarding guide: a cover with the repository facts and
/// a linked table of contents, then one section per topic. Sections without content are left out.
/// </summary>
internal sealed class QuestPdfSummaryRenderer : ISummaryPdfRenderer
{
    private const string Accent = "#0F6CBD";
    private const string Ink = "#17202B";
    private const string Muted = "#5A6472";
    private const string Rule = "#DCE1E8";
    private const string Shade = "#F0F3F7";

    public byte[] Render(RepoSummary summary, SummaryDocumentInfo info)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(info);

        var sections = BuildSections(summary);

        return Document.Create(document =>
            {
                document.Page(page =>
                {
                    ConfigurePage(page);
                    page.Content().Element(c => ComposeCover(c, info, sections));
                    page.Footer().Element(ComposeFooter);
                });

                document.Page(page =>
                {
                    ConfigurePage(page);
                    page.Header().Element(c => ComposeHeader(c, info));
                    page.Content().PaddingTop(12).Column(column =>
                    {
                        column.Spacing(22);
                        foreach (var section in sections)
                        {
                            // EnsureSpace starts the section on a new page rather than stranding its heading at the bottom.
                            column.Item().Section(section.Id).EnsureSpace(110).Column(body =>
                            {
                                body.Spacing(8);
                                body.Item().Text(section.Title).FontSize(16).SemiBold().FontColor(Accent);
                                section.Compose(body);
                            });
                        }
                    });
                    page.Footer().Element(ComposeFooter);
                });
            })
            .WithMetadata(new DocumentMetadata
            {
                Title = $"{info.RepoFullName} onboarding guide",
                Author = "RepoLens",
                Subject = $"Summary of {info.RepoFullName} at {info.CommitSha}",
                CreationDate = info.GeneratedAt,
                ModifiedDate = info.GeneratedAt,
            })
            .GeneratePdf();
    }

    private static void ConfigurePage(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.Margin(48);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(style => style.FontSize(10).FontColor(Ink).LineHeight(1.4f));
    }

    private static void ComposeCover(IContainer container, SummaryDocumentInfo info, IReadOnlyList<PdfSection> sections)
    {
        container.PaddingTop(60).Column(column =>
        {
            column.Spacing(10);
            column.Item().Text("ONBOARDING GUIDE").FontSize(10).LetterSpacing(0.12f).SemiBold().FontColor(Muted);
            column.Item().Text(info.RepoFullName).FontSize(28).Bold().FontColor(Ink);

            if (!string.IsNullOrWhiteSpace(info.Description))
            {
                column.Item().Text(info.Description).FontSize(12).FontColor(Muted);
            }

            column.Item().PaddingTop(18).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(130);
                    columns.RelativeColumn();
                });

                void Fact(string label, string value)
                {
                    table.Cell().BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(5).Text(label).FontColor(Muted);
                    table.Cell().BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(5).Text(value);
                }

                Fact("Repository", info.HtmlUrl);
                Fact("Branch", info.Branch);
                Fact("Commit", info.CommitSha);
                Fact("Primary language", info.PrimaryLanguage ?? "Unknown");
                Fact("Files analyzed", info.FilesAnalyzed.ToString("N0", CultureInfo.InvariantCulture));
                Fact("Generated", info.GeneratedAt.ToUniversalTime().ToString("d MMMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture));
            });

            column.Item().PaddingTop(28).Text("Contents").FontSize(13).SemiBold().FontColor(Accent);
            foreach (var (section, number) in sections.Select((s, i) => (s, i + 1)))
            {
                column.Item().SectionLink(section.Id).Row(row =>
                {
                    row.ConstantItem(24).Text($"{number}.").FontColor(Muted);
                    row.RelativeItem().Text(section.Title);
                    row.ConstantItem(40).AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontColor(Muted));
                        text.BeginPageNumberOfSection(section.Id);
                    });
                });
            }

            column.Item().PaddingTop(28).Text(
                    "Generated automatically from the repository's source. Verify important details against the code.")
                .FontSize(9).Italic().FontColor(Muted);
        });
    }

    private static void ComposeHeader(IContainer container, SummaryDocumentInfo info)
    {
        container.BorderBottom(0.5f).BorderColor(Rule).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Text(info.RepoFullName).SemiBold().FontColor(Ink);
            row.ConstantItem(160).AlignRight().Text($"{info.Branch} @ {Short(info.CommitSha)}").FontSize(9).FontColor(Muted);
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Text(text =>
        {
            text.DefaultTextStyle(style => style.FontSize(9).FontColor(Muted));
            text.Span("RepoLens · page ");
            text.CurrentPageNumber();
            text.Span(" of ");
            text.TotalPages();
        });
    }

    private static List<PdfSection> BuildSections(RepoSummary summary)
    {
        var sections = new List<PdfSection>();

        void Add(string id, string title, bool hasContent, Action<ColumnDescriptor> compose)
        {
            if (hasContent)
            {
                sections.Add(new PdfSection(id, title, compose));
            }
        }

        Add("overview", "What this project does", Has(summary.Overview) || Has(summary.Audience), body =>
        {
            Paragraphs(body, summary.Overview);
            if (Has(summary.Audience))
            {
                body.Item().Text(text =>
                {
                    text.Span("Who it is for: ").SemiBold();
                    text.Span(summary.Audience);
                });
            }
        });

        Add("tech-stack", "Tech stack", summary.TechStack.Count > 0, body =>
            Table(body, ["Technology", "Category", "Used for"], [3, 2, 6],
                summary.TechStack.Select(t => new[] { t.Name, t.Category, t.Purpose })));

        Add("architecture", "Architecture", Has(summary.Architecture), body => Paragraphs(body, summary.Architecture));

        Add("modules", "Module map", summary.Modules.Count > 0, body =>
            Table(body, ["Folder", "Responsibility", "Key files"], [3, 5, 4],
                summary.Modules.Select(m => new[] { m.Path, m.Responsibility, string.Join("\n", m.KeyFiles) })));

        Add("flows", "Key flows", summary.KeyFlows.Count > 0, body =>
        {
            foreach (var flow in summary.KeyFlows)
            {
                body.Item().PaddingTop(4).Text(flow.Name).FontSize(12).SemiBold();
                if (Has(flow.Summary))
                {
                    body.Item().Text(flow.Summary).FontColor(Muted);
                }

                Numbered(body, flow.Steps);
            }
        });

        Add("entry-points", "Entry points", summary.EntryPoints.Count > 0, body =>
            Table(body, ["File", "Why it matters"], [4, 7], summary.EntryPoints.Select(e => new[] { e.Path, e.Description })));

        Add("data-model", "Data model and storage", Has(summary.DataModel), body => Paragraphs(body, summary.DataModel));

        Add("configuration", "Configuration", summary.Configuration.Count > 0, body =>
            Table(body, ["Setting", "What it controls"], [4, 7], summary.Configuration.Select(c => new[] { c.Name, c.Description })));

        Add("build-and-run", "Build, run and test", summary.BuildAndRun.Count > 0, body =>
        {
            foreach (var (step, number) in summary.BuildAndRun.Select((s, i) => (s, i + 1)))
            {
                body.Item().Row(row =>
                {
                    row.ConstantItem(22).Text($"{number}.").SemiBold().FontColor(Accent);
                    row.RelativeItem().Column(inner =>
                    {
                        inner.Spacing(4);
                        inner.Item().Text(step.Title);
                        if (Has(step.Command))
                        {
                            inner.Item().Background(Shade).Padding(6).Text(step.Command).FontSize(9);
                        }
                    });
                });
            }
        });

        Add("where-to-start", "Where to start reading", summary.WhereToStart.Count > 0, body => Numbered(body, summary.WhereToStart));

        Add("glossary", "Glossary", summary.Glossary.Count > 0, body =>
            Table(body, ["Term", "Meaning"], [3, 8], summary.Glossary.Select(g => new[] { g.Term, g.Definition })));

        return sections;
    }

    private static void Paragraphs(ColumnDescriptor body, string text)
    {
        foreach (var paragraph in text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            body.Item().Text(paragraph);
        }
    }

    private static void Numbered(ColumnDescriptor body, IReadOnlyList<string> items)
    {
        foreach (var (item, number) in items.Select((s, i) => (s, i + 1)))
        {
            body.Item().Row(row =>
            {
                row.ConstantItem(22).Text($"{number}.").FontColor(Accent);
                row.RelativeItem().Text(item);
            });
        }
    }

    private static void Table(ColumnDescriptor body, string[] headers, float[] widths, IEnumerable<string[]> rows)
    {
        body.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var width in widths)
                {
                    columns.RelativeColumn(width);
                }
            });

            table.Header(header =>
            {
                foreach (var title in headers)
                {
                    header.Cell().Background(Shade).PaddingVertical(5).PaddingHorizontal(6).Text(title).FontSize(9).SemiBold().FontColor(Muted);
                }
            });

            foreach (var row in rows)
            {
                foreach (var cell in row)
                {
                    table.Cell().BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(5).PaddingHorizontal(6).Text(cell ?? string.Empty);
                }
            }
        });
    }

    private static bool Has(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;

    private sealed record PdfSection(string Id, string Title, Action<ColumnDescriptor> Compose);
}
