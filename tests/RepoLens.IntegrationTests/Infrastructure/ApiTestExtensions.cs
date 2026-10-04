using System.Net.Http.Json;
using System.Text.Json;
using RepoLens.Api.Contracts;
using RepoLens.Application.Common.Models;
using RepoLens.Application.Repos;
using RepoLens.Application.Summaries;
using RepoLens.Domain.Summaries;

namespace RepoLens.IntegrationTests.Infrastructure;

internal static class ApiTestExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(RepoLensApiFactory.Json))!;

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static async Task<PagedResult<RepoDto>> ListReposAsync(this HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<PagedResult<RepoDto>>($"/api/v1/repos{query}", RepoLensApiFactory.Json))!;

    public static async Task<RepoDto> GetRepoByNameAsync(this HttpClient client, string name) =>
        (await client.ListReposAsync("?pageSize=100")).Items.Single(r => r.Name == name);

    public static async Task<SummaryRequestResponse> StartSummaryAsync(this HttpClient client, Guid repoId, bool force = false)
    {
        var response = await client.PostAsync($"/api/v1/repos/{repoId}/summaries{(force ? "?force=true" : string.Empty)}", content: null);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<SummaryRequestResponse>();
    }

    /// <summary>Polls the status endpoint until the job finishes (or the condition holds).</summary>
    public static async Task<SummaryJobDto> WaitForJobAsync(
        this HttpClient client,
        Guid jobId,
        Func<SummaryJobDto, bool>? until = null,
        TimeSpan? timeout = null)
    {
        until ??= job => job.Status is SummaryStatus.Completed or SummaryStatus.Failed;
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));

        while (true)
        {
            var job = (await client.GetFromJsonAsync<SummaryJobDto>($"/api/v1/summaries/{jobId}", RepoLensApiFactory.Json))!;
            if (until(job))
            {
                return job;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Job {jobId} is still {job.Status}/{job.Stage} ({job.Progress}%) after the timeout; error: {job.Error?.Code}");
            }

            await Task.Delay(100);
        }
    }

    /// <summary>Summarizes a repository end to end and returns the completed job.</summary>
    public static async Task<SummaryJobDto> SummarizeAsync(this HttpClient client, Guid repoId)
    {
        var started = await client.StartSummaryAsync(repoId);
        var job = await client.WaitForJobAsync(started.Job.Id);
        Assert.Equal(SummaryStatus.Completed, job.Status);
        return job;
    }
}
