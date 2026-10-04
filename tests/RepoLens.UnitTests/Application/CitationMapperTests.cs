using RepoLens.Application.Common.Abstractions.Search;
using RepoLens.Application.Questions;
using RepoLens.Domain.Indexing;

namespace RepoLens.UnitTests.Application;

public sealed class CitationMapperTests
{
    [Fact]
    public void Markers_map_to_sources_in_order_of_first_mention_without_duplicates()
    {
        var repo = TestData.Repo();
        var sources = new[]
        {
            Source(repo.Id, "src/Program.cs", 1, 20),
            Source(repo.Id, "src/Orders/Order Service.cs", 5, 40),
            Source(repo.Id, "README.md", 1, 10),
        };

        var citations = CitationMapper.FromAnswer("Start in Program [1]. Orders are handled in [2, 1] and [9].", repo, TestData.Sha, sources);

        Assert.Equal(["src/Program.cs", "src/Orders/Order Service.cs"], citations.Select(c => c.Path));
        Assert.Equal($"https://github.com/octo/shop/blob/{TestData.Sha}/src/Orders/Order%20Service.cs#L5-L40", citations[1].Url);
    }

    [Fact]
    public void An_answer_without_markers_has_no_citations()
    {
        var repo = TestData.Repo();

        Assert.Empty(CitationMapper.FromAnswer("I could not find that in the code.", repo, TestData.Sha, [Source(repo.Id, "a.cs", 1, 2)]));
    }

    private static ScoredChunk Source(Guid repoId, string path, int start, int end) =>
        new(new CodeChunk(repoId, TestData.Sha, path, "csharp", start, end, "code"), 0.9);
}
