using RepoLens.Application.Source;

namespace RepoLens.UnitTests.Application;

public sealed class SourceTests
{
    private static readonly SourceOptions Limits = new() { MaxFileBytes = 1_000, MaxFiles = 100, MaxTotalBytes = 100_000, MaxTreePaths = 100 };

    private readonly DefaultCodeFileFilter _filter = new(TestData.Options(Limits));

    [Theory]
    [InlineData("src/Program.cs", true)]
    [InlineData("README.md", true)]
    [InlineData(".github/workflows/ci.yml", true)]
    [InlineData("Dockerfile", true)]
    [InlineData(".env.example", true)]
    [InlineData("packages/ui/src/button.tsx", true)]
    [InlineData("node_modules/react/index.js", false)]
    [InlineData("src/bin/Debug/app.dll", false)]
    [InlineData("package-lock.json", false)]
    [InlineData("web/dist/app.min.js", false)]
    [InlineData(".env", false)]
    [InlineData(".vscode/settings.json", false)]
    [InlineData("assets/logo.png", false)]
    [InlineData("src/Form1.Designer.cs", false)]
    public void Filter_keeps_source_and_skips_dependencies_output_and_secrets(string path, bool expected) =>
        Assert.Equal(expected, _filter.IsEligible(path, 100));

    [Fact]
    public void Filter_skips_files_over_the_size_limit() =>
        Assert.False(_filter.IsEligible("src/Big.cs", Limits.MaxFileBytes + 1));

    [Theory]
    [InlineData("README.md", FileKind.Readme)]
    [InlineData("src/Api/Api.csproj", FileKind.Manifest)]
    [InlineData("package.json", FileKind.Manifest)]
    [InlineData("src/Api/Program.cs", FileKind.EntryPoint)]
    [InlineData("src/Api/appsettings.json", FileKind.Configuration)]
    [InlineData("Dockerfile", FileKind.Infrastructure)]
    [InlineData(".github/workflows/ci.yml", FileKind.Infrastructure)]
    [InlineData("tests/Api.Tests/OrderTests.cs", FileKind.Test)]
    [InlineData("src/Orders/OrderServiceTests.cs", FileKind.Test)]
    [InlineData("src/Orders/latest.cs", FileKind.Source)]
    [InlineData("docs/architecture.md", FileKind.Documentation)]
    [InlineData("src/Orders/OrderService.cs", FileKind.Source)]
    public void Classifier_recognizes_the_role_of_a_file(string path, FileKind expected) =>
        Assert.Equal(expected, FileClassifier.Classify(path));

    [Fact]
    public void Loader_strips_the_zipball_root_folder_and_reads_text_files()
    {
        using var zip = TestData.Zip(new Dictionary<string, byte[]>
        {
            ["README.md"] = TestData.Utf8("# Shop\nA sample."),
            ["src/Program.cs"] = TestData.Utf8("var app = 1;\nConsole.WriteLine(app);\n"),
            ["node_modules/x/index.js"] = TestData.Utf8("ignored"),
        });

        var snapshot = Loader().Load(zip);

        Assert.Equal(["README.md", "src/Program.cs"], snapshot.Files.Select(f => f.Path));
        Assert.Contains("node_modules/x/index.js", snapshot.AllPaths);
        Assert.Equal(1, snapshot.SkippedFiles);
        Assert.Equal(2, snapshot.Files.Single(f => f.Path == "src/Program.cs").LineCount);
    }

    [Fact]
    public void Loader_skips_binary_and_invalid_utf8_content_and_unsafe_paths()
    {
        using var zip = TestData.Zip(new Dictionary<string, byte[]>
        {
            ["src/data.json"] = [0x7B, 0x00, 0x7D],
            ["src/latin1.txt"] = [0x63, 0x61, 0x66, 0xE9],
            ["../evil.cs"] = TestData.Utf8("class Evil {}"),
            ["src/ok.cs"] = TestData.Utf8("class Ok {}"),
        });

        var snapshot = Loader().Load(zip);

        Assert.Equal(["src/ok.cs"], snapshot.Files.Select(f => f.Path));
    }

    [Fact]
    public void Loader_stops_at_the_file_limit_and_reports_truncation()
    {
        var files = Enumerable.Range(0, 5).ToDictionary(i => $"src/f{i}.cs", i => TestData.Utf8($"class F{i} {{}}"));
        using var zip = TestData.Zip(files);
        var loader = new SourceSnapshotLoader(_filter, TestData.Options(new SourceOptions { MaxFiles = 3, MaxFileBytes = 1_000, MaxTotalBytes = 100_000, MaxTreePaths = 100 }));

        var snapshot = loader.Load(zip);

        Assert.Equal(3, snapshot.Files.Count);
        Assert.True(snapshot.Truncated);
    }

    private SourceSnapshotLoader Loader() => new(_filter, TestData.Options(Limits));
}
