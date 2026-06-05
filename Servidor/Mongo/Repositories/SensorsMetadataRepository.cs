using MongoDB.Driver;
using Servidor.Mongo.Models;

namespace Servidor.Mongo.Repositories;

public class SensorsMetadataRepository
{
    private readonly IMongoCollection<SensorMetadataDocument> sensorsMetadata;

    public SensorsMetadataRepository(MongoDbContext context)
    {
        sensorsMetadata = context.Database.GetCollection<SensorMetadataDocument>("sensors_metadata");
    }

    public async Task<SensorMetadataDocument?> GetBySensorIdAsync(
        string sensorId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<SensorMetadataDocument>.Filter.Eq(metadata => metadata.SensorId, sensorId);
        return await sensorsMetadata.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<SensorMetadataDocument?> UpsertObservationAsync(
        string sensorId,
        string zone,
        string type,
        DateTime observedAt,
        CancellationToken cancellationToken = default)
    {
        var observedAtUtc = DateTime.SpecifyKind(observedAt, DateTimeKind.Utc);

        var filter = Builders<SensorMetadataDocument>.Filter.And(
            Builders<SensorMetadataDocument>.Filter.Eq(metadata => metadata.SensorId, sensorId),
            Builders<SensorMetadataDocument>.Filter.Eq(metadata => metadata.Type, type));
        var update = Builders<SensorMetadataDocument>.Update
            .Set(metadata => metadata.Zone, zone)
            .Set(metadata => metadata.Type, type)
            .Set(metadata => metadata.LastReadingAt, observedAtUtc)
            .SetOnInsert(metadata => metadata.SensorId, sensorId)
            .SetOnInsert(metadata => metadata.FirstReadingAt, observedAtUtc)
            .Inc(metadata => metadata.TotalReadings, 1);

        var options = new FindOneAndUpdateOptions<SensorMetadataDocument>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        return await sensorsMetadata.FindOneAndUpdateAsync(filter, update, options, cancellationToken);
    }
}
