using System.Collections.Frozen;

namespace RepoLens.Application.Source;

/// <summary>Assigns a <see cref="FileKind"/> from the path, so the summary reads the most telling files first.</summary>
public static class FileClassifier
{
    private static readonly FrozenSet<string> ManifestNames = new[]
    {
        "package.json", "global.json", "directory.build.props", "directory.packages.props", "nuget.config",
        "pom.xml", "build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts",
        "go.mod", "cargo.toml", "pyproject.toml", "requirements.txt", "setup.py", "setup.cfg", "pipfile",
        "gemfile", "composer.json", "mix.exs", "pubspec.yaml", "package.swift", "cmakelists.txt", "makefile",
        "deno.json", "tsconfig.json", "angular.json", "turbo.json", "nx.json", "lerna.json", "pnpm-workspace.yaml",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ManifestExtensions = new[]
    {
        ".csproj", ".fsproj", ".vbproj", ".sln", ".slnx",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> EntryPointNames = new[]
    {
        "program.cs", "startup.cs", "main.go", "main.rs", "lib.rs", "main.py", "__main__.py", "app.py", "manage.py",
        "wsgi.py", "asgi.py", "main.ts", "main.tsx", "main.js", "index.ts", "index.tsx", "index.js", "app.ts",
        "app.tsx", "app.js", "server.ts", "server.js", "main.kt", "main.dart", "main.swift", "main.c", "main.cpp",
        "layout.tsx", "_app.tsx", "_app.js", "app.vue", "main.java",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> InfrastructureNames = new[]
    {
        "dockerfile", "containerfile", "docker-compose.yml", "docker-compose.yaml", "compose.yml", "compose.yaml",
        "procfile", "azure-pipelines.yml", ".gitlab-ci.yml", "jenkinsfile", "vercel.json", "netlify.toml",
        "fly.toml", "render.yaml", "app.yaml", "skaffold.yaml", "chart.yaml",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static FileKind Classify(string path)
    {
        var lowerPath = path.ToLowerInvariant();
        var fileName = SourceLanguages.FileName(lowerPath);
        var extension = Path.GetExtension(fileName);
        var language = SourceLanguages.Detect(path);

        if (fileName.StartsWith("readme", StringComparison.Ordinal))
        {
            return FileKind.Readme;
        }

        if (ManifestNames.Contains(fileName) || ManifestExtensions.Contains(extension))
        {
            return FileKind.Manifest;
        }

        if (InfrastructureNames.Contains(fileName)
            || fileName.StartsWith("dockerfile", StringComparison.Ordinal)
            || lowerPath.StartsWith(".github/workflows/", StringComparison.Ordinal)
            || lowerPath.StartsWith(".circleci/", StringComparison.Ordinal)
            || language is "terraform" or "bicep"
            || ContainsSegment(lowerPath, "k8s") || ContainsSegment(lowerPath, "helm") || ContainsSegment(lowerPath, "deploy") || ContainsSegment(lowerPath, "infra"))
        {
            return FileKind.Infrastructure;
        }

        if (IsTest(lowerPath, fileName, SourceLanguages.FileName(path)))
        {
            return FileKind.Test;
        }

        if (language == "markdown" || language == "text")
        {
            return FileKind.Documentation;
        }

        if (IsConfiguration(lowerPath, fileName, language))
        {
            return FileKind.Configuration;
        }

        if (EntryPointNames.Contains(fileName) || fileName.EndsWith("application.java", StringComparison.Ordinal)
            || (fileName == "main.go" && lowerPath.StartsWith("cmd/", StringComparison.Ordinal)))
        {
            return FileKind.EntryPoint;
        }

        return language is "json" or "yaml" or "xml" or "toml" or "ini" or "properties" or "dotenv"
            ? FileKind.Configuration
            : language is null ? FileKind.Other : FileKind.Source;
    }

    /// <summary>Lower is read first.</summary>
    public static int Priority(FileKind kind) => kind switch
    {
        FileKind.Readme => 0,
        FileKind.Manifest => 1,
        FileKind.EntryPoint => 2,
        FileKind.Configuration => 3,
        FileKind.Infrastructure => 4,
        FileKind.Source => 5,
        FileKind.Documentation => 6,
        FileKind.Test => 7,
        _ => 8,
    };

    private static bool IsTest(string lowerPath, string fileName, string originalFileName) =>
        ContainsSegment(lowerPath, "test") || ContainsSegment(lowerPath, "tests") || ContainsSegment(lowerPath, "__tests__")
        || ContainsSegment(lowerPath, "spec") || ContainsSegment(lowerPath, "e2e")
        || lowerPath.Split('/').Any(segment => segment.EndsWith(".tests", StringComparison.Ordinal) || segment.EndsWith(".test", StringComparison.Ordinal))
        || fileName.Contains(".test.", StringComparison.Ordinal) || fileName.Contains(".spec.", StringComparison.Ordinal)
        || originalFileName.EndsWith("Tests.cs", StringComparison.Ordinal) || originalFileName.EndsWith("Test.cs", StringComparison.Ordinal)
        || originalFileName.EndsWith("Test.java", StringComparison.Ordinal) || originalFileName.EndsWith("Tests.java", StringComparison.Ordinal)
        || fileName.EndsWith("_test.go", StringComparison.Ordinal) || fileName.StartsWith("test_", StringComparison.Ordinal);

    private static bool IsConfiguration(string lowerPath, string fileName, string? language) =>
        fileName.StartsWith("appsettings", StringComparison.Ordinal)
        || fileName.StartsWith("application.", StringComparison.Ordinal)
        || fileName.StartsWith(".env", StringComparison.Ordinal)
        || fileName is "settings.py" or "config.py" or "next.config.js" or "next.config.mjs" or "next.config.ts"
            or "vite.config.ts" or "vite.config.js" or "webpack.config.js" or "tailwind.config.js" or "tailwind.config.ts"
        || fileName.EndsWith(".config", StringComparison.Ordinal)
        || (ContainsSegment(lowerPath, "config") && language is "json" or "yaml" or "toml" or "ini" or "properties");

    private static bool ContainsSegment(string lowerPath, string segment)
    {
        var index = lowerPath.IndexOf(segment, StringComparison.Ordinal);
        while (index >= 0)
        {
            var startsAtBoundary = index == 0 || lowerPath[index - 1] == '/';
            var end = index + segment.Length;
            var endsAtFolderBoundary = end < lowerPath.Length && lowerPath[end] == '/';
            if (startsAtBoundary && endsAtFolderBoundary)
            {
                return true;
            }

            index = lowerPath.IndexOf(segment, index + 1, StringComparison.Ordinal);
        }

        return false;
    }
}
