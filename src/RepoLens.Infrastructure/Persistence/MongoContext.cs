using MongoDB.Driver;
using RepoLens.Domain.Auth;
using RepoLens.Domain.Conversations;
using RepoLens.Domain.Indexing;
using RepoLens.Domain.Repos;
using RepoLens.Domain.Summaries;
using RepoLens.Domain.Users;

namespace RepoLens.Infrastructure.Persistence;

/// <summary>Typed access to every collection RepoLens uses.</summary>
public sealed class MongoContext(IMongoDatabase database)
{
    public static class Names
    {
        public const string Users = "users";
        public const string Repos = "repos";
        public const string RepoIndexes = "repo_indexes";
        public const string SummaryJobs = "summary_jobs";
        public const string CodeChunks = "code_chunks";
        public const string Conversations = "conversations";
        public const string AuthCodes = "auth_codes";
        public const string RefreshTokens = "refresh_tokens";
        public const string DataProtectionKeys = "dataprotection_keys";
    }

    public IMongoDatabase Database { get; } = database;

    public IMongoCollection<User> Users => Database.GetCollection<User>(Names.Users);

    public IMongoCollection<Repo> Repos => Database.GetCollection<Repo>(Names.Repos);

    public IMongoCollection<RepoIndex> RepoIndexes => Database.GetCollection<RepoIndex>(Names.RepoIndexes);

    public IMongoCollection<SummaryJob> SummaryJobs => Database.GetCollection<SummaryJob>(Names.SummaryJobs);

    public IMongoCollection<CodeChunk> CodeChunks => Database.GetCollection<CodeChunk>(Names.CodeChunks);

    public IMongoCollection<Conversation> Conversations => Database.GetCollection<Conversation>(Names.Conversations);

    public IMongoCollection<AuthCode> AuthCodes => Database.GetCollection<AuthCode>(Names.AuthCodes);

    public IMongoCollection<RefreshToken> RefreshTokens => Database.GetCollection<RefreshToken>(Names.RefreshTokens);
}
