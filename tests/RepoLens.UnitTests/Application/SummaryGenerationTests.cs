using RepoLens.Application.Source;
using RepoLens.Application.Summaries;
using RepoLens.Application.Summaries.Generation;
using RepoLens.Domain.Summaries;

namespace RepoLens.UnitTests.Application;

public sealed class SummaryGenerationTests
{
    [Theory]
    [InlineData("Program.cs", ModulePlanner.RootModule)]
    [InlineData("api/handlers/orders.go", "api")]
    [InlineData("src/Orders/OrderService.cs", "src/Orders")]
    [InlineData("packages/ui/button.tsx", "packages/ui")]
    [InlineData("src/index.ts", "src")]
    public void Module_name_uses_the_folder_below_container_folders(string path, string expected) =>
        Assert.Equal(expected, ModulePlanner.ModuleNameOf(path));

    [Fact]
    public void Small_repositories_are_summarized_in_a_single_pass()
    {
        var snapshot = Snapshot(TestData.File("README.md", "# Shop"), TestData.File("src/Program.cs", "class P {}"));

        var plan = Planner(singlePassMaxChars: 10_000).Plan(snapshot);

        Assert.True(plan.SinglePass);
        Assert.Equal("README.md", plan.Readme?.Path);
    }

    [Fact]
    public void Large_repositories_are_split_into_modules_within_the_budget()
    {
        var big = new string('x', 3_000);
        var snapshot = Snapshot(
            TestData.File("src/Orders/A.cs", big),
            TestData.File("src/Orders/B.cs", big),
            TestData.File("src/Billing/C.cs", big),
            TestData.File("tools/D.cs", big),
            TestData.File("scripts/E.cs", big));

        var plan = Planner(singlePassMaxChars: 1_000, maxModules: 3, maxCharsPerModule: 4_000).Plan(snapshot);

        Assert.False(plan.SinglePass);
        Assert.Equal(3, plan.Modules.Count);
        Assert.Equal("src/Orders", plan.Modules[0].Name);
        Assert.Contains(plan.Modules, m => m.Name == ModulePlanner.OtherModule);
        Assert.All(plan.Modules, m => Assert.True(m.Files.Sum(f => f.Content.Length) <= 4_000 + 20));
        Assert.Contains(plan.Modules[0].OtherPaths, p => p.StartsWith("src/Orders/", StringComparison.Ordinal));
    }

    [Fact]
    public void Normalizer_removes_nulls_blanks_and_oversized_lists()
    {
        var summary = new RepoSummary
        {
            Overview = "  Shop API  ",
            TechStack = null!,
            Modules = Enumerable.Range(0, 50).Select(i => new ModuleSummary { Path = $"m{i}", Responsibility = "r", KeyFiles = null! }).ToList(),
            WhereToStart = ["", "  ", "Program.cs"],
        };

        var normalized = SummaryNormalizer.Normalize(summary);

        Assert.Equal("Shop API", normalized.Overview);
        Assert.Empty(normalized.TechStack);
        Assert.Equal(30, normalized.Modules.Count);
        Assert.All(normalized.Modules, m => Assert.NotNull(m.KeyFiles));
        Assert.Equal(["Program.cs"], normalized.WhereToStart);
    }

    private static SourceSnapshot Snapshot(params SourceFile[] files) =>
        new(files, files.Select(f => f.Path).ToList(), skippedFiles: 0, truncated: false);

    private static ModulePlanner Planner(int singlePassMaxChars, int maxModules = 16, int maxCharsPerModule = 40_000) =>
        new(TestData.Options(new SummaryGenerationOptions
        {
            SinglePassMaxChars = singlePassMaxChars,
            MaxModules = maxModules,
            MaxCharsPerModule = maxCharsPerModule,
        }));
}
