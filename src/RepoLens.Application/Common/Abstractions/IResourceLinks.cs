namespace RepoLens.Application.Common.Abstractions;

/// <summary>Builds links to API resources for responses. The API owns its routes, so it implements this.</summary>
public interface IResourceLinks
{
    string SummaryStatus(Guid jobId);

    string SummaryPdf(Guid jobId);
}
