using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Servidor.Mongo.Models;

public class ReadingDocument
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

    [BsonElement("value")]
    public double Value { get; set; }

    [BsonElement("unit")]
    public string Unit { get; set; } = string.Empty;

    [BsonElement("timestamp")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime Timestamp { get; set; }

    [BsonElement("gatewayId")]
    public string GatewayId { get; set; } = string.Empty;

    [BsonElement("messageId")]
    [BsonIgnoreIfNull]
    public string? MessageId { get; set; }

    [BsonElement("originalMessageFormat")]
    public string OriginalMessageFormat { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
