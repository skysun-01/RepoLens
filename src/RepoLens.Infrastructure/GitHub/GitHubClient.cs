using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoLens.Application.Common.Abstractions.GitHub;
using RepoLens.Domain.Repos;

namespace RepoLens.Infrastructure.GitHub;

/// <summary>Typed <see cref="HttpClient"/> for the GitHub REST API. Retries and timeouts come from the resilience pipeline.</summary>
internal sealed partial class GitHubClient(HttpClient http, IOptions<GitHubOptions> options, ILogger<GitHubClient> logger) : IGitHubClient
{
    private const int PageSize = 100;

    public async Task<IReadOnlyList<RepoDetails>> GetRepositoriesAsync(string accessToken, CancellationToken cancellationToken)
    {
        var max = options.Value.MaxRepositories;
        var result = new List<RepoDetails>();

        for (var page = 1; result.Count < max; page++)
        {
            var uri = $"user/repos?per_page={PageSize}&page={page}&affiliation=owner,collaborator,organization_member&sort=pushed&direction=desc";
            using var request = CreateRequest(HttpMethod.Get, uri, accessToken);
            using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, "your repositories", cancellationToken);

            var items = await response.Content.ReadFromJsonAsync(GitHubJsonContext.Default.ListGitHubRepositoryJson, cancellationToken) ?? [];
            result.AddRange(items.Select(ToDetails));

            if (items.Count < PageSize)
            {
                break;
            }
        }

        if (result.Count >= max)
        {
            LogRepoLimitReached(max);
        }

        return result.Take(max).ToList();
    }

    public async Task<string> GetBranchHeadShaAsync(string accessToken, string owner, string repo, string branch, CancellationToken cancellationToken)
    {
        var uri = $"repos/{Escape(owner)}/{Escape(repo)}/commits/{EscapeRef(branch)}";
        using var request = CreateRequest(HttpMethod.Get, uri, accessToken);
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/vnd.github.sha");

        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, $"branch '{branch}' of {owner}/{repo}", cancellationToken);
        var sha = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();

        if (sha.Length is < 7 or > 64 || !sha.All(Uri.IsHexDigit))
        {
            throw new GitHubUnavailableException($"GitHub returned an unexpected commit id for {owner}/{repo}.");
        }

        return sha;
    }

    public async Task<Stream> DownloadArchiveAsync(string accessToken, string owner, string repo, string commitSha, CancellationToken cancellationToken)
    {
        var maxBytes = options.Value.MaxArchiveBytes;
        var uri = $"repos/{Escape(owner)}/{Escape(repo)}/zipball/{Escape(commitSha)}";
        using var request = CreateRequest(HttpMethod.Get, uri, accessToken);

        // GitHub redirects to codeload.github.com with a short-lived signed URL; HttpClient follows it
        // and drops the Authorization header on the cross-host redirect.
        using var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, $"{owner}/{repo}", cancellationToken);

        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw new RepositoryTooLargeException(maxBytes);
        }

        var tempFile = new FileStream(
            Path.Combine(Path.GetTempPath(), $"repolens-{Guid.NewGuid():N}.zip"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await CopyWithLimitAsync(source, tempFile, maxBytes, cancellationToken);
            tempFile.Position = 0;
            return tempFile;
        }
        catch
        {
            await tempFile.DisposeAsync();
            throw;
        }
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81_920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new RepositoryTooLargeException(maxBytes);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion, string what, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, completion, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new GitHubUnavailableException("GitHub could not be reached. Try again shortly.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubUnavailableException("GitHub took too long to respond. Try again shortly.", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            throw MapError(response, what);
        }
    }

    private Exception MapError(HttpResponseMessage response, string what)
    {
        var status = response.StatusCode;
        LogGitHubError((int)status, response.RequestMessage?.RequestUri?.AbsolutePath ?? "?");

        if (status == HttpStatusCode.TooManyRequests || (status == HttpStatusCode.Forbidden && IsRateLimited(response)))
        {
            return new GitHubRateLimitException(RateLimitReset(response));
        }

        return status switch
        {
            HttpStatusCode.Unauthorized => new GitHubAuthorizationException(),
            // 403 without rate-limit headers: missing scope or organization SAML enforcement; signing in again fixes both.
            HttpStatusCode.Forbidden => new GitHubAuthorizationException(),
            HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity => new GitHubNotFoundException(what),
            HttpStatusCode.Conflict => new RepositoryEmptyException(),
            _ => new GitHubUnavailableException($"GitHub returned {(int)status} for {what}. Try again shortly."),
        };
    }

    private static bool IsRateLimited(HttpResponseMessage response) =>
        (response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.FirstOrDefault() == "0")
        || response.Headers.RetryAfter is not null;

    private static DateTimeOffset? RateLimitReset(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-ratelimit-reset", out var values)
        && long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch)
            ? DateTimeOffset.FromUnixTimeSeconds(epoch)
            : null;

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    /// <summary>Git refs such as <c>release/v2</c> keep their slashes; each part is escaped.</summary>
    private static string EscapeRef(string gitRef) => string.Join('/', gitRef.Split('/').Select(Uri.EscapeDataString));

    private static RepoDetails ToDetails(GitHubRepositoryJson json) => new(
        json.Id,
        json.Owner?.Login ?? json.FullName.Split('/')[0],
        json.Name,
        json.FullName,
        json.Description,
        json.Language,
        json.Private,
        json.Fork,
        json.Archived,
        json.DefaultBranch ?? "main",
        json.HtmlUrl,
        json.StargazersCount,
        json.Size,
        json.PushedAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GitHub returned {StatusCode} for {Path}")]
    private partial void LogGitHubError(int statusCode, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopped listing repositories at the configured maximum of {Max}")]
    private partial void LogRepoLimitReached(int max);
}

internal sealed record GitHubOwnerJson([property: JsonPropertyName("login")] string Login);

internal sealed record GitHubRepositoryJson(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("owner")] GitHubOwnerJson? Owner,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("private")] bool Private,
    [property: JsonPropertyName("fork")] bool Fork,
    [property: JsonPropertyName("archived")] bool Archived,
    [property: JsonPropertyName("default_branch")] string? DefaultBranch,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("stargazers_count")] int StargazersCount,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt);

[JsonSerializable(typeof(List<GitHubRepositoryJson>))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
