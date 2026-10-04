using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using RepoLens.Infrastructure.Persistence;

namespace RepoLens.Infrastructure.Security;

/// <summary>
/// Stores the ASP.NET Core Data Protection key ring in MongoDB, so the API and the Worker (and every
/// replica) share the keys that encrypt GitHub tokens.
/// </summary>
internal sealed class MongoXmlRepository(MongoContext context) : IXmlRepository
{
    private IMongoCollection<KeyDocument> Keys => context.Database.GetCollection<KeyDocument>(MongoContext.Names.DataProtectionKeys);

    public IReadOnlyCollection<XElement> GetAllElements() =>
        Keys.Find(FilterDefinition<KeyDocument>.Empty)
            .ToList()
            .Select(k => XElement.Parse(k.Xml))
            .ToList();

    public void StoreElement(XElement element, string friendlyName) =>
        Keys.InsertOne(new KeyDocument
        {
            Id = ObjectId.GenerateNewId(),
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
            CreatedAt = DateTime.UtcNow,
        });

    internal sealed class KeyDocument
    {
        [BsonId]
        public ObjectId Id { get; set; }

        public string FriendlyName { get; set; } = string.Empty;

        public string Xml { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }
}
