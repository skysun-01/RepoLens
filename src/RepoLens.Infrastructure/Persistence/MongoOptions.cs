using System.ComponentModel.DataAnnotations;

namespace RepoLens.Infrastructure.Persistence;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    /// <summary>MongoDB, MongoDB Atlas or Azure Cosmos DB for MongoDB (vCore) connection string.</summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    [Required]
    public string DatabaseName { get; set; } = "repolens";
}
