using MongoDB.Driver;
using Servidor.Mongo.Models;

namespace Servidor.Mongo.Repositories;

public class AnalysesRepository
{
    private readonly IMongoCollection<AnalysisDocument> analyses;

    public AnalysesRepository(MongoDbContext context)
    {
        analyses = context.Database.GetCollection<AnalysisDocument>("analyses");
    }

    public async Task InsertAsync(AnalysisDocument analysis, CancellationToken cancellationToken = default)
    {
        analysis.CreatedAt = analysis.CreatedAt == default ? DateTime.UtcNow : analysis.CreatedAt;
        await analyses.InsertOneAsync(analysis, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<AnalysisDocument>> FindAsync(
        string? type = null,
        string? zone = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var builder = Builders<AnalysisDocument>.Filter;
        var filters = new List<FilterDefinition<AnalysisDocument>>();

        if (!string.IsNullOrWhiteSpace(type))
        {
            filters.Add(builder.Eq(analysis => analysis.Type, type));
        }

        if (!string.IsNullOrWhiteSpace(zone))
        {
            filters.Add(builder.Eq(analysis => analysis.Zone, zone));
        }

        var filter = filters.Count == 0 ? builder.Empty : builder.And(filters);
        IFindFluent<AnalysisDocument, AnalysisDocument> query =
            analyses.Find(filter).SortByDescending(analysis => analysis.CreatedAt);

        if (limit is > 0)
        {
            query = query.Limit(limit.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }
}
