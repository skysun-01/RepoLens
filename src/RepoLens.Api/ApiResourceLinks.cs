using RepoLens.Application.Common.Abstractions;

namespace RepoLens.Api;

/// <summary>Links in responses are relative to the API's base URL.</summary>
internal sealed class ApiResourceLinks : IResourceLinks
{
    public string SummaryStatus(Guid jobId) => $"/api/v1/summaries/{jobId}";

    public string SummaryPdf(Guid jobId) => $"/api/v1/summaries/{jobId}/pdf";
}
