using RepoLens.Application.Indexing;

namespace RepoLens.UnitTests.Application;

public sealed class CodeChunkerTests
{
    private static CodeChunker Chunker(int size = 200, int overlap = 0) =>
        new(TestData.Options(new IndexingOptions { MaxChunkChars = size, OverlapChars = overlap }));

    [Fact]
    public void Small_file_becomes_one_chunk_covering_every_line()
    {
        var file = TestData.File("src/a.cs", "line1\nline2\nline3");

        var chunk = Assert.Single(Chunker().Chunk(Guid.NewGuid(), TestData.Sha, file));

        Assert.Equal(1, chunk.StartLine);
        Assert.Equal(3, chunk.EndLine);
        Assert.Equal("line1\nline2\nline3", chunk.Content);
    }

    [Fact]
    public void Chunks_cover_the_file_with_exact_line_numbers()
    {
        var lines = Enumerable.Range(1, 60).Select(i => $"var value{i:D2} = {i}; // padding text").ToList();
        var file = TestData.File("src/b.cs", string.Join('\n', lines));

        var chunks = Chunker(size: 200).Chunk(Guid.NewGuid(), TestData.Sha, file);

        Assert.True(chunks.Count > 1);
        Assert.Equal(1, chunks[0].StartLine);
        Assert.Equal(60, chunks[^1].EndLine);
        foreach (var chunk in chunks)
        {
            var expected = string.Join('\n', lines.Skip(chunk.StartLine - 1).Take(chunk.EndLine - chunk.StartLine + 1));
            Assert.Equal(expected, chunk.Content);
        }

        // Without overlap, consecutive chunks are contiguous.
        for (var i = 1; i < chunks.Count; i++)
        {
            Assert.Equal(chunks[i - 1].EndLine + 1, chunks[i].StartLine);
        }
    }

    [Fact]
    public void Overlap_repeats_the_tail_of_the_previous_chunk()
    {
        var file = TestData.File("src/c.cs", string.Join('\n', Enumerable.Range(1, 40).Select(i => $"statement number {i:D2};")));

        var chunks = Chunker(size: 200, overlap: 50).Chunk(Guid.NewGuid(), TestData.Sha, file);

        Assert.True(chunks.Count > 1);
        Assert.True(chunks[1].StartLine <= chunks[0].EndLine);
    }

    [Fact]
    public void Chunk_prefers_to_end_at_a_code_boundary()
    {
        var body = string.Join('\n', Enumerable.Range(1, 8).Select(i => $"    int field{i} = {i};"));
        var content = $"public class First\n{{\n{body}\n}}\npublic class Second\n{{\n{body}\n}}";
        var file = TestData.File("src/d.cs", content);

        var chunks = Chunker(size: 260).Chunk(Guid.NewGuid(), TestData.Sha, file);

        Assert.Contains(chunks, c => c.Content.StartsWith("public class Second", StringComparison.Ordinal));
    }

    [Fact]
    public void Windows_line_endings_are_handled()
    {
        var file = TestData.File("src/e.cs", "a\r\nb\r\nc");

        var chunk = Assert.Single(Chunker().Chunk(Guid.NewGuid(), TestData.Sha, file));

        Assert.Equal(3, chunk.EndLine);
        Assert.Equal("a\nb\nc", chunk.Content);
    }
}
