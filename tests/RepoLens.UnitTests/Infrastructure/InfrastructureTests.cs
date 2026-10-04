using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using RepoLens.Application.Common.Abstractions.Documents;
using RepoLens.Domain.Summaries;
using RepoLens.Domain.Users;
using RepoLens.Infrastructure.AI;
using RepoLens.Infrastructure.Documents;
using RepoLens.Infrastructure.Messaging;
using RepoLens.Infrastructure.Search;
using RepoLens.Infrastructure.Security;

namespace RepoLens.UnitTests.Infrastructure;

public sealed class InfrastructureTests
{
    [Fact]
    public void Pdf_renderer_produces_a_pdf_for_a_full_summary()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var summary = new RepoSummary
        {
            Overview = "Shop is an e-commerce API.\n\nIt sells things.",
            Audience = "Developers of the storefront.",
            TechStack = [new TechStackItem { Name = "ASP.NET Core", Category = "Framework", Purpose = "HTTP API" }],
            Architecture = "Controllers call services, services call repositories.",
            Modules = [new ModuleSummary { Path = "src/Api", Responsibility = "HTTP endpoints", KeyFiles = ["src/Api/Program.cs"] }],
            KeyFlows = [new KeyFlow { Name = "Checkout", Summary = "Places an order.", Steps = ["POST /orders hits OrdersController", "OrderService validates stock"] }],
            EntryPoints = [new EntryPoint { Path = "src/Api/Program.cs", Description = "Starts the web host." }],
            DataModel = "Orders and products in PostgreSQL.",
            Configuration = [new ConfigSetting { Name = "ConnectionStrings__Db", Description = "Database connection." }],
            BuildAndRun = [new SetupStep { Title = "Run the API", Command = "dotnet run --project src/Api" }],
            WhereToStart = ["Read Program.cs", "Then OrdersController"],
            Glossary = [new GlossaryTerm { Term = "SKU", Definition = "Stock keeping unit." }],
        };

        var pdf = new QuestPdfSummaryRenderer().Render(summary, Info());

        Assert.True(pdf.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Pdf_renderer_handles_an_empty_summary()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var pdf = new QuestPdfSummaryRenderer().Render(new RepoSummary(), Info());

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Theory]
    [InlineData("https://res.openai.azure.com")]
    [InlineData("https://res.openai.azure.com/")]
    [InlineData("https://res.openai.azure.com/openai")]
    [InlineData("https://res.openai.azure.com/openai/v1/")]
    public void Azure_endpoint_is_normalized_to_the_v1_api(string endpoint) =>
        Assert.Equal("https://res.openai.azure.com/openai/v1/", AiServiceCollectionExtensions.AzureV1Endpoint(endpoint).ToString());

    [Fact]
    public async Task Issued_jwt_validates_and_carries_the_user_id()
    {
        var options = new JwtOptions { SigningKey = "unit-test-signing-key-0123456789abcdef-xyz" };
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var user = User.Register(77, "octo", "Octo Cat", null, null, "enc", "repo", time.GetUtcNow());

        var issued = new JwtAccessTokenIssuer(TestData.Options(options), time).Issue(user);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.Token, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(options.SigningKeyBytes()),
        });

        Assert.True(result.IsValid);
        Assert.Equal(user.Id.ToString(), result.Claims[JwtRegisteredClaimNames.Sub]);
        Assert.Equal("octo", result.Claims[JwtAccessTokenIssuer.LoginClaim]);
    }

    [Fact]
    public void Short_signing_keys_are_rejected()
    {
        var errors = new JwtOptions { SigningKey = "too-short" }.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(new object()));

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Token_protector_round_trips_and_does_not_store_plain_text()
    {
        var protector = new DataProtectionTokenProtector(new EphemeralDataProtectionProvider());

        var protectedValue = protector.Protect("gho_secret");

        Assert.DoesNotContain("gho_secret", protectedValue, StringComparison.Ordinal);
        Assert.Equal("gho_secret", protector.Unprotect(protectedValue));
    }

    [Fact]
    public void Secret_tokens_are_random_and_hashes_are_stable()
    {
        var generator = new SecretTokenGenerator();

        var a = generator.Generate();
        Assert.NotEqual(a, generator.Generate());
        Assert.Equal(generator.Hash(a), generator.Hash(a));
        Assert.Equal(64, generator.Hash(a).Length);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(10, 120)]
    public void Retry_delay_doubles_up_to_the_maximum(int deliveryCount, int expectedSeconds)
    {
        var options = new MessagingOptions { RetryBaseDelay = TimeSpan.FromSeconds(5), RetryMaxDelay = TimeSpan.FromMinutes(2) };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), options.RetryDelay(deliveryCount));
    }

    [Fact]
    public void Cosine_similarity_ranks_identical_vectors_highest()
    {
        float[] query = [1, 0, 1];

        Assert.Equal(1, InAppVectorSearch.CosineSimilarity(query, [2, 0, 2]), precision: 6);
        Assert.Equal(0, InAppVectorSearch.CosineSimilarity(query, [0, 1, 0]), precision: 6);
        Assert.Equal(0, InAppVectorSearch.CosineSimilarity(query, [1, 1]), precision: 6);
    }

    private static SummaryDocumentInfo Info() =>
        new("octo/shop", "A sample shop", "https://github.com/octo/shop", "main", TestData.Sha, "C#", 42, TestData.Now);
}
