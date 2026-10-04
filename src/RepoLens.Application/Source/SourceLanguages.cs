using System.Collections.Frozen;

namespace RepoLens.Application.Source;

/// <summary>Maps file names and extensions to language ids used for chunking and display.</summary>
public static class SourceLanguages
{
    private static readonly FrozenDictionary<string, string> ByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = "csharp", ["csx"] = "csharp", ["fs"] = "fsharp", ["vb"] = "vb",
        ["csproj"] = "xml", ["fsproj"] = "xml", ["vbproj"] = "xml", ["props"] = "xml", ["targets"] = "xml",
        ["sln"] = "text", ["slnx"] = "xml", ["razor"] = "razor", ["cshtml"] = "razor",
        ["java"] = "java", ["kt"] = "kotlin", ["kts"] = "kotlin", ["scala"] = "scala", ["groovy"] = "groovy", ["gradle"] = "groovy",
        ["ts"] = "typescript", ["tsx"] = "typescript", ["mts"] = "typescript", ["cts"] = "typescript",
        ["js"] = "javascript", ["jsx"] = "javascript", ["mjs"] = "javascript", ["cjs"] = "javascript",
        ["vue"] = "vue", ["svelte"] = "svelte", ["astro"] = "astro",
        ["py"] = "python", ["pyi"] = "python", ["ipynb"] = "json",
        ["go"] = "go", ["rs"] = "rust", ["rb"] = "ruby", ["php"] = "php",
        ["c"] = "c", ["h"] = "c", ["cpp"] = "cpp", ["cc"] = "cpp", ["cxx"] = "cpp", ["hpp"] = "cpp", ["hh"] = "cpp",
        ["swift"] = "swift", ["m"] = "objectivec", ["mm"] = "objectivec", ["dart"] = "dart",
        ["ex"] = "elixir", ["exs"] = "elixir", ["erl"] = "erlang", ["hs"] = "haskell", ["clj"] = "clojure",
        ["lua"] = "lua", ["r"] = "r", ["jl"] = "julia", ["pl"] = "perl", ["sol"] = "solidity",
        ["md"] = "markdown", ["mdx"] = "markdown", ["rst"] = "text", ["txt"] = "text", ["adoc"] = "text",
        ["yml"] = "yaml", ["yaml"] = "yaml", ["json"] = "json", ["jsonc"] = "json", ["toml"] = "toml", ["xml"] = "xml",
        ["ini"] = "ini", ["cfg"] = "ini", ["conf"] = "ini", ["properties"] = "properties", ["env"] = "dotenv",
        ["sql"] = "sql", ["graphql"] = "graphql", ["gql"] = "graphql", ["proto"] = "protobuf", ["prisma"] = "prisma",
        ["sh"] = "shell", ["bash"] = "shell", ["zsh"] = "shell", ["ps1"] = "powershell", ["psm1"] = "powershell",
        ["bat"] = "batch", ["cmd"] = "batch",
        ["html"] = "html", ["htm"] = "html", ["css"] = "css", ["scss"] = "scss", ["sass"] = "scss", ["less"] = "less",
        ["tf"] = "terraform", ["bicep"] = "bicep", ["hcl"] = "terraform",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> ByFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["dockerfile"] = "dockerfile",
        ["containerfile"] = "dockerfile",
        ["makefile"] = "makefile",
        ["gemfile"] = "ruby",
        ["rakefile"] = "ruby",
        ["procfile"] = "text",
        ["jenkinsfile"] = "groovy",
        ["vagrantfile"] = "ruby",
        [".env.example"] = "dotenv",
        [".env.sample"] = "dotenv",
        [".gitlab-ci.yml"] = "yaml",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the language id, or null when the file type is not readable source or text.</summary>
    public static string? Detect(string path)
    {
        var fileName = FileName(path);

        if (ByFileName.TryGetValue(fileName, out var language))
        {
            return language;
        }

        if (fileName.StartsWith("dockerfile.", StringComparison.OrdinalIgnoreCase))
        {
            return "dockerfile";
        }

        var dot = fileName.LastIndexOf('.');
        return dot > 0 && ByExtension.TryGetValue(fileName[(dot + 1)..], out language) ? language : null;
    }

    public static string FileName(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }
}
