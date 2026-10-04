using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using RepoLens.Domain.Common;
using RepoLens.Domain.Conversations;
using RepoLens.Domain.Repos;

namespace RepoLens.Infrastructure.Persistence;

/// <summary>
/// Maps domain types to BSON without attributes, so the Domain project stays free of MongoDB.
/// Elements are camelCase, enums are strings, GUIDs use the standard UUID representation and
/// timestamps are stored as BSON dates.
/// </summary>
internal static class MongoMappings
{
    private static readonly Lock Gate = new();
    private static bool _registered;

    public static void Register()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
            BsonSerializer.TryRegisterSerializer(new DateTimeOffsetSerializer(BsonType.DateTime));

            var conventions = new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new IgnoreExtraElementsConvention(true),
                new EnumRepresentationConvention(BsonType.String),
            };
            ConventionRegistry.Register("RepoLens", conventions, type => type.Namespace?.StartsWith("RepoLens", StringComparison.Ordinal) == true);

            BsonClassMap.TryRegisterClassMap<Entity>(map =>
            {
                map.AutoMap();
                map.MapIdMember(e => e.Id);
            });

            BsonClassMap.TryRegisterClassMap<Conversation>(map =>
            {
                map.AutoMap();
                map.MapField("_messages").SetElementName("messages");
            });

            BsonClassMap.TryRegisterClassMap<RepoIndex>(map =>
            {
                map.AutoMap();
                map.MapIdMember(i => i.RepoId);
            });

            _registered = true;
        }
    }
}
