using System.Net;
using System.Net.Http.Json;
using RepoLens.Application.Common.Abstractions.GitHub;
using RepoLens.Domain.Summaries;
using RepoLens.IntegrationTests.Infrastructure;

namespace RepoLens.IntegrationTests;

public sealed class ResilienceAndIsolationTests(RepoLensApiFactory factory) : IClassFixture<RepoLensApiFactory>, IDisposable
{
    public void Dispose() => factory.GitHub.ArchiveFailure = (_, _) => null;

    [Fact]
    public async Task A_permanent_github_error_fails_the_job_with_a_readable_reason()
    {
        var client = await factory.CreateSignedInClientAsync();
        var blog = await client.GetRepoByNameAsync("blog");
        factory.GitHub.ArchiveFailure = (repo, _) => repo == "blog" ? new GitHubNotFoundException("octo/blog") : null;

        var started = await client.StartSummaryAsync(blog.Id);
        var job = await client.WaitForJobAsync(started.Job.Id);

        Assert.Equal(SummaryStatus.Failed, job.Status);
        Assert.Equal("github_not_found", job.Error?.Code);
        Assert.Equal(1, job.Attempts);

        var row = await client.GetRepoByNameAsync("blog");
        Assert.Equal(SummaryStatus.Failed, row.Summary.Status);
        Assert.Equal("github_not_found", row.Summary.ErrorCode);
        Assert.Null(row.Summary.LatestPdf);
    }

    [Fact]
    public async Task A_transient_error_is_retried_by_the_message_bus_and_then_succeeds()
    {
        var client = await factory.CreateSignedInClientAsync();
        var shop = await client.GetRepoByNameAsync("shop");
        var firstFailingDownload = factory.GitHub.ArchiveDownloads + 1;
        factory.GitHub.ArchiveFailure = (_, attempt) => attempt == firstFailingDownload ? new GitHubUnavailableException("GitHub is down") : null;

        var started = await client.StartSummaryAsync(shop.Id, force: true);
        var job = await client.WaitForJobAsync(started.Job.Id);

        Assert.Equal(SummaryStatus.Completed, job.Status);
        Assert.Equal(2, job.Attempts);
        Assert.Null(job.Error);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_repositories_jobs_or_pdfs()
    {
        var alice = await factory.CreateSignedInClientAsync("alice");
        var bob = await factory.CreateSignedInClientAsync("bob");
        var aliceShop = await alice.GetRepoByNameAsync("shop");
        var aliceJob = await alice.SummarizeAsync(aliceShop.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/repos/{aliceShop.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/v1/repos/{aliceShop.Id}/summaries", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/summaries/{aliceJob.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/summaries/{aliceJob.Id}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/v1/repos/{aliceShop.Id}/questions", new { question = "What is this?" })).StatusCode);

        // Bob has his own copy of the same GitHub repository, with its own id.
        var bobShop = await bob.GetRepoByNameAsync("shop");
        Assert.NotEqual(aliceShop.Id, bobShop.Id);
        Assert.Null(bobShop.Summary.Status);
    }

    [Fact]
    public async Task A_revoked_github_token_asks_the_user_to_sign_in_again()
    {
        var client = await factory.CreateSignedInClientAsync("revoked", gitHubToken: FakeGitHubClient.RevokedToken);

        var response = await client.GetAsync("/api/v1/repos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("github_reauth_required", await response.ProblemCodeAsync());
    }
}
