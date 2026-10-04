using System.ComponentModel;

namespace RepoLens.Domain.Summaries;

/// <summary>
/// The onboarding summary of a repository. The AI model fills this fixed shape, and the PDF
/// renderer lays it out, so every PDF has the same sections whatever the repository.
/// The descriptions double as instructions to the model.
/// </summary>
public sealed record RepoSummary
{
    [Description("Two to four sentences: what this project does, for whom, and why it exists.")]
    public string Overview { get; init; } = string.Empty;

    [Description("Who uses or runs this project and in what situation.")]
    public string Audience { get; init; } = string.Empty;

    [Description("Languages, frameworks, libraries, databases and tools the project relies on.")]
    public IReadOnlyList<TechStackItem> TechStack { get; init; } = [];

    [Description("How the project is structured: layers, main components and how they talk to each other. Plain prose, one to three short paragraphs.")]
    public string Architecture { get; init; } = string.Empty;

    [Description("Top-level folders or modules and what each is responsible for.")]
    public IReadOnlyList<ModuleSummary> Modules { get; init; } = [];

    [Description("The most important end-to-end flows (for example handling a request, a job, a CLI command), step by step.")]
    public IReadOnlyList<KeyFlow> KeyFlows { get; init; } = [];

    [Description("Files where execution starts or that a new developer should read first.")]
    public IReadOnlyList<EntryPoint> EntryPoints { get; init; } = [];

    [Description("Main data structures, database tables or documents, and how data is stored. Empty if not applicable.")]
    public string DataModel { get; init; } = string.Empty;

    [Description("Configuration settings and environment variables the project reads.")]
    public IReadOnlyList<ConfigSetting> Configuration { get; init; } = [];

    [Description("How to build, run and test the project, as ordered steps with commands where known.")]
    public IReadOnlyList<SetupStep> BuildAndRun { get; init; } = [];

    [Description("Suggested reading order for a new developer's first day.")]
    public IReadOnlyList<string> WhereToStart { get; init; } = [];

    [Description("Project-specific terms and abbreviations a newcomer would not know.")]
    public IReadOnlyList<GlossaryTerm> Glossary { get; init; } = [];
}

public sealed record TechStackItem
{
    [Description("Name of the language, framework, library or tool.")]
    public string Name { get; init; } = string.Empty;

    [Description("One of: Language, Framework, Library, Database, Messaging, Cloud, Tooling, Testing, Other.")]
    public string Category { get; init; } = string.Empty;

    [Description("What the project uses it for.")]
    public string Purpose { get; init; } = string.Empty;
}

public sealed record ModuleSummary
{
    [Description("Folder path relative to the repository root, for example src/api.")]
    public string Path { get; init; } = string.Empty;

    [Description("What this module is responsible for.")]
    public string Responsibility { get; init; } = string.Empty;

    [Description("The most important files in this module.")]
    public IReadOnlyList<string> KeyFiles { get; init; } = [];
}

public sealed record KeyFlow
{
    [Description("Short name of the flow, for example 'User sign-in' or 'Order checkout'.")]
    public string Name { get; init; } = string.Empty;

    [Description("One sentence on what triggers the flow and what it produces.")]
    public string Summary { get; init; } = string.Empty;

    [Description("Ordered steps. Each step says what happens and, where possible, in which file.")]
    public IReadOnlyList<string> Steps { get; init; } = [];
}

public sealed record EntryPoint
{
    [Description("File path relative to the repository root.")]
    public string Path { get; init; } = string.Empty;

    [Description("Why this file matters.")]
    public string Description { get; init; } = string.Empty;
}

public sealed record ConfigSetting
{
    [Description("Setting or environment variable name.")]
    public string Name { get; init; } = string.Empty;

    [Description("What it controls. Never include secret values.")]
    public string Description { get; init; } = string.Empty;
}

public sealed record SetupStep
{
    [Description("What this step achieves.")]
    public string Title { get; init; } = string.Empty;

    [Description("Command to run, if any. Empty when the step has no command.")]
    public string Command { get; init; } = string.Empty;
}

public sealed record GlossaryTerm
{
    public string Term { get; init; } = string.Empty;

    public string Definition { get; init; } = string.Empty;
}
