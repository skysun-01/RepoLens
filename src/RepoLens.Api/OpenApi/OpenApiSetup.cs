using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RepoLens.Api.OpenApi;

/// <summary>OpenAPI document with a JWT bearer scheme, so Swagger UI shows an Authorize button.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "RepoLens API",
            Version = "v1",
            Description = """
                Sign in with GitHub, list your repositories, generate onboarding PDFs (event-driven), and ask questions about the code.

                **Getting a token locally:** open `/api/v1/auth/github/login` in the browser. After GitHub sign-in the
                development callback page shows an access token; click **Authorize** here and paste it.
                """,
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access token from POST /api/v1/auth/token (or the development callback page).",
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, document)] = [],
        });

        return Task.CompletedTask;
    }
}
