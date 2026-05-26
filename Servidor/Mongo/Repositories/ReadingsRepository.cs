using MongoDB.Driver;
using Servidor.Mongo.Models;

namespace Servidor.Mongo.Repositories;

public class ReadingsRepository
{
    private readonly IMongoCollection<ReadingDocument> readings;

    public ReadingsRepository(MongoDbContext context)
    {
        readings = context.Database.GetCollection<ReadingDocument>("readings");
    }

    public async Task InsertAsync(ReadingDocument reading, CancellationToken cancellationToken = default)
    {
        reading.CreatedAt = reading.CreatedAt == default ? DateTime.UtcNow : reading.CreatedAt;
        await readings.InsertOneAsync(reading, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<ReadingDocument>> FindAsync(
        string? sensorId = null,
        string? zone = null,
        string? type = null,
        DateTime? from = null,
        DateTime? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var builder = Builders<ReadingDocument>.Filter;
        var filters = new List<FilterDefinition<ReadingDocument>>();

        if (!string.IsNullOrWhiteSpace(sensorId))
        {
            filters.Add(builder.Eq(reading => reading.SensorId, sensorId));
        }

        if (!string.IsNullOrWhiteSpace(zone))
        {
            filters.Add(builder.Eq(reading => reading.Zone, zone));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            filters.Add(builder.Eq(reading => reading.Type, type));
        }

        if (from.HasValue)
        {
            filters.Add(builder.Gte(reading => reading.Timestamp, DateTime.SpecifyKind(from.Value, DateTimeKind.Utc)));
        }

        if (to.HasValue)
        {
            filters.Add(builder.Lte(reading => reading.Timestamp, DateTime.SpecifyKind(to.Value, DateTimeKind.Utc)));
        }

        var filter = filters.Count == 0 ? builder.Empty : builder.And(filters);
        IFindFluent<ReadingDocument, ReadingDocument> query =
            readings.Find(filter).SortByDescending(reading => reading.Timestamp);

        if (limit is > 0)
        {
            query = query.Limit(limit.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }
}
