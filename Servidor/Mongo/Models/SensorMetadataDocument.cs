using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Servidor.Mongo.Models;

public class SensorMetadataDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("sensorId")]
    public string SensorId { get; set; } = string.Empty;

    [BsonElement("zone")]
    public string Zone { get; set; } = string.Empty;

    [BsonElement("type")]
    public string Type { get; set; } = string.Empty;

    [BsonElement("firstReadingAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime FirstReadingAt { get; set; }

    [BsonElement("lastReadingAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastReadingAt { get; set; }

    [BsonElement("totalReadings")]
    public long TotalReadings { get; set; }
}
