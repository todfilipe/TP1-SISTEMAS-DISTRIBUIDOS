using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Servidor.Mongo.Models;

public class AnalysisDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("sensorId")]
    [BsonIgnoreIfNull]
    public string? SensorId { get; set; }

    [BsonElement("zone")]
    public string Zone { get; set; } = string.Empty;

    [BsonElement("type")]
    public string Type { get; set; } = string.Empty;

    [BsonElement("windowStart")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime WindowStart { get; set; }

    [BsonElement("windowEnd")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime WindowEnd { get; set; }

    [BsonElement("average")]
    public double Average { get; set; }

    [BsonElement("standardDeviation")]
    public double StandardDeviation { get; set; }

    [BsonElement("median")]
    public double Median { get; set; }

    [BsonElement("outlierCount")]
    public int OutlierCount { get; set; }

    [BsonElement("trendClassification")]
    public string TrendClassification { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("rawGrpcResultSerialized")]
    public string RawGrpcResultSerialized { get; set; } = string.Empty;
}
