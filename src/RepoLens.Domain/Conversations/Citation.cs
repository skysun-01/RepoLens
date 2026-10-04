namespace RepoLens.Domain.Conversations;

/// <summary>A source file range an answer is based on, with a link to that range on GitHub.</summary>
public sealed record Citation(string Path, int StartLine, int EndLine, string Url);
