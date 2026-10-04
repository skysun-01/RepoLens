using System.Net;
using System.Text;
using RepoLens.Api.Contracts;
using RepoLens.Application.Summaries;
using RepoLens.Domain.Summaries;
using RepoLens.IntegrationTests.Infrastructure;

namespace RepoLens.IntegrationTests;

/// <summary>The Summarize button end to end: 202 → background job → PDF in GridFS → download.</summary>
public sealed class SummaryFlowTests(RepoLensApiFactory factory) : IClassFixture<RepoLensApiFactory>
{
    [Fact]
    public async Task Repository_table_is_synced_from_github_on_first_use()
    {
        var client = await factory.CreateSignedInClientAsync();

        var page = await client.ListReposAsync();

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("shop", page.Items[0].Name);
        Assert.Null(page.Items[0].Summary.Status);
        Assert.Null(page.Items[0].Index);
    }

    [Fact]
    public async Task Repository_table_supports_search_sort_and_validation()
    {
        var client = await factory.CreateSignedInClientAsync();

        var search = await client.ListReposAsync("?search=blog");
        var byName = await client.ListReposAsync("?sort=name&pageSize=1");
        var invalid = await client.GetAsync("/api/v1/repos?pageSize=0&sort=size");

        Assert.Equal("blog", Assert.Single(search.Items).Name);
        Assert.Equal("blog", Assert.Single(byName.Items).Name);
        Assert.Equal(2, byName.TotalPages);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var body = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("pageSize", body, StringComparison.Ordinal);
        Assert.Contains("sort", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Summarize_returns_202_then_produces_a_downloadable_pdf()
    {
        var client = await factory.CreateSignedInClientAsync();
        var shop = await client.GetRepoByNameAsync("shop");

        var response = await client.PostAsync($"/api/v1/repos/{shop.Id}/summaries", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.ReadAsync<SummaryRequestResponse>();
        Assert.Equal(SummaryRequestOutcome.Created, accepted.Outcome);
        Assert.Equal($"/api/v1/summaries/{accepted.Job.Id}", response.Headers.Location!.AbsolutePath);

        var job = await client.WaitForJobAsync(accepted.Job.Id);
        Assert.Equal(SummaryStatus.Completed, job.Status);
        Assert.Equal(100, job.Progress);
        Assert.Equal(factory.GitHub.HeadSha, job.CommitSha);
        Assert.NotNull(job.Pdf);
        Assert.Equal("shop-summary-1111111.pdf", job.Pdf.FileName);

        var inline = await client.GetAsync(job.Pdf.Url);
        Assert.Equal(HttpStatusCode.OK, inline.StatusCode);
        Assert.Equal("application/pdf", inline.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", inline.Content.Headers.ContentDisposition?.DispositionType);
        var bytes = await inline.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));

        var download = await client.GetAsync(job.Pdf.Url + "?download=true");
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);

        var row = await client.GetRepoByNameAsync("shop");
        Assert.Equal(SummaryStatus.Completed, row.Summary.Status);
        Assert.Equal(job.Id, row.Summary.LatestPdf?.JobId);
        Assert.Equal(job.Pdf.Url, row.Summary.LatestPdf?.Url);
        Assert.NotNull(row.Index);
        Assert.True(row.Index.ChunkCount > 0);
    }

    [Fact]
    public async Task Summarizing_an_unchanged_commit_reuses_the_existing_pdf_unless_forced()
    {
        var client = await factory.CreateSignedInClientAsync();
        var shop = await client.GetRepoByNameAsync("shop");
        var first = await client.SummarizeAsync(shop.Id);

        var again = await client.PostAsync($"/api/v1/repos/{shop.Id}/summaries", content: null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var reused = await again.ReadAsync<SummaryRequestResponse>();
        Assert.Equal(SummaryRequestOutcome.ReusedCompleted, reused.Outcome);
        Assert.Equal(first.Id, reused.Job.Id);

        var forced = await client.StartSummaryAsync(shop.Id, force: true);
        Assert.Equal(SummaryRequestOutcome.Created, forced.Outcome);
        Assert.NotEqual(first.Id, forced.Job.Id);
        Assert.Equal(SummaryStatus.Completed, (await client.WaitForJobAsync(forced.Job.Id)).Status);

        var history = await client.GetStringAsync($"/api/v1/repos/{shop.Id}/summaries");
        Assert.Contains(first.Id.ToString(), history, StringComparison.Ordinal);
        Assert.Contains(forced.Job.Id.ToString(), history, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clicking_summarize_twice_while_running_returns_the_same_job()
    {
        var client = await factory.CreateSignedInClientAsync();
        var blog = await client.GetRepoByNameAsync("blog");
        factory.Chat.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var first = await client.StartSummaryAsync(blog.Id);
            var running = await client.WaitForJobAsync(first.Job.Id, job => job.Stage == SummaryStage.GeneratingSummary);
            Assert.Equal(SummaryStatus.Processing, running.Status);

            var second = await client.PostAsync($"/api/v1/repos/{blog.Id}/summaries", content: null);
            Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
            var duplicate = await second.ReadAsync<SummaryRequestResponse>();
            Assert.Equal(SummaryRequestOutcome.AlreadyInProgress, duplicate.Outcome);
            Assert.Equal(first.Job.Id, duplicate.Job.Id);

            var pdfTooEarly = await client.GetAsync($"/api/v1/summaries/{first.Job.Id}/pdf");
            Assert.Equal(HttpStatusCode.Conflict, pdfTooEarly.StatusCode);
            Assert.Equal("summary_not_ready", await pdfTooEarly.ProblemCodeAsync());

            factory.Chat.Gate.SetResult();
            Assert.Equal(SummaryStatus.Completed, (await client.WaitForJobAsync(first.Job.Id)).Status);
        }
        finally
        {
            factory.Chat.Gate?.TrySetResult();
            factory.Chat.Gate = null;
        }
    }
}
