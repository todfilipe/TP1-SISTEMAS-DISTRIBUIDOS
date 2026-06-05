using Analysis;
using Google.Protobuf;
using MongoDB.Driver;
using Servidor.Mongo.Models;
using Servidor.Mongo.Repositories;
using Shared;
using System.Globalization;

namespace Servidor;

internal sealed class MongoReadingPersister
{
    private static readonly TimeSpan DefaultWriteTimeout = TimeSpan.FromSeconds(5);

    private readonly ReadingsRepository? _readingsRepository;
    private readonly AnalysesRepository? _analysesRepository;
    private readonly SensorsMetadataRepository? _sensorsMetadataRepository;
    private readonly TimeSpan _writeTimeout;

    internal MongoReadingPersister(
        ReadingsRepository? readingsRepository,
        AnalysesRepository? analysesRepository,
        SensorsMetadataRepository? sensorsMetadataRepository,
        TimeSpan? writeTimeout = null)
    {
        _readingsRepository = readingsRepository;
        _analysesRepository = analysesRepository;
        _sensorsMetadataRepository = sensorsMetadataRepository;
        _writeTimeout = writeTimeout ?? DefaultWriteTimeout;
    }

    internal async Task<bool> PersistirLeituraMongoAsync(
        string sensorId,
        string tipoDado,
        string valor,
        string zona,
        string timestamp,
        string gatewayId,
        string mensagemOriginal,
        string? messageId = null)
    {
        if (_readingsRepository == null)
        {
            Console.WriteLine("[AVISO][MongoDB] Repositorio de leituras indisponivel. Leitura guardada apenas no SQLite local.");
            return false;
        }

        bool readingPersisted = false;

        try
        {
            if (!double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out double valorNumerico))
            {
                Console.WriteLine($"[ERRO][MongoDB] Valor invalido para persistencia MongoDB: {valor}. Leitura guardada apenas no SQLite local.");
                return false;
            }

            DateTime timestampUtc = ProtocolHelpers.ParseDateTimeOrUtcNow(timestamp);

            var reading = new ReadingDocument
            {
                SensorId = sensorId,
                Zone = zona,
                Type = tipoDado,
                Value = valorNumerico,
                Unit = SensorTypes.GetUnitForType(tipoDado, string.Empty),
                Timestamp = timestampUtc,
                GatewayId = gatewayId,
                MessageId = string.IsNullOrWhiteSpace(messageId) ? null : messageId,
                OriginalMessageFormat = mensagemOriginal,
                CreatedAt = DateTime.UtcNow
            };

            using var cts = new CancellationTokenSource(_writeTimeout);
            await _readingsRepository.InsertAsync(reading, cts.Token);
            readingPersisted = true;
            Console.WriteLine($"[Servidor][MongoDB] Leitura persistida em readings: sensor={sensorId}, tipo={tipoDado}, zona={zona}, timestamp={timestampUtc:yyyy-MM-ddTHH:mm:ssZ}.");

            if (_sensorsMetadataRepository == null)
            {
                Console.WriteLine("[AVISO][MongoDB] Repositorio de metadados indisponivel. Leitura persistida em readings, mas sensors_metadata nao foi atualizado.");
                return true;
            }

            await _sensorsMetadataRepository.UpsertObservationAsync(sensorId, zona, tipoDado, timestampUtc, cts.Token);
            Console.WriteLine($"[Servidor][MongoDB] Metadados do sensor atualizados: sensor={sensorId}.");
            return true;
        }
        catch (OperationCanceledException ex)
        {
            string impacto = readingPersisted
                ? "Leitura persistida em readings, mas a atualizacao de sensors_metadata nao foi confirmada."
                : "Leitura guardada apenas no SQLite local.";
            Console.WriteLine($"[ERRO][MongoDB] Timeout ao persistir leitura apos {_writeTimeout.TotalSeconds:0}s: {ex.Message}. {impacto}");
            return readingPersisted;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            Console.WriteLine($"[Servidor][MongoDB] Leitura duplicada ignorada em readings: messageId={messageId}.");
            return true;
        }
        catch (Exception ex)
        {
            string impacto = readingPersisted
                ? "Leitura persistida em readings, mas a atualizacao de sensors_metadata falhou."
                : "Leitura guardada apenas no SQLite local.";
            Console.WriteLine($"[ERRO][MongoDB] Falha ao persistir leitura no MongoDB: {ex.GetType().Name}: {ex.Message}. {impacto}");
            return readingPersisted;
        }
    }

    internal async Task<AnalysisDocument?> PersistirAnaliseMongoAsync(AnalysisResult result, AnalysisRequest request)
    {
        if (_analysesRepository == null)
        {
            Console.WriteLine("[AVISO][MongoDB] Repositorio de analises indisponivel. Resultado mantido apenas em memoria/ficheiro local.");
            return null;
        }

        try
        {
            DateTime windowStart = ProtocolHelpers.ResolveWindowBoundary(request.DateFrom, request.Readings.Select(r => r.Timestamp), useMinimum: true);
            DateTime windowEnd = ProtocolHelpers.ResolveWindowBoundary(request.DateTo, request.Readings.Select(r => r.Timestamp), useMinimum: false);

            var analysis = new AnalysisDocument
            {
                SensorId = string.IsNullOrWhiteSpace(request.SensorId) ? null : request.SensorId,
                Zone = request.Zone,
                Type = request.Type,
                WindowStart = windowStart,
                WindowEnd = windowEnd,
                Average = result.Mean != 0 ? result.Mean : result.ComputedAverage,
                StandardDeviation = result.StdDev,
                Median = result.Median,
                Percentile25 = result.Percentile25,
                Percentile75 = result.Percentile75,
                Percentile95 = result.Percentile95,
                Min = result.Min,
                Max = result.Max,
                OutlierCount = result.OutliersCount,
                TrendSlope = result.TrendSlope,
                TrendClassification = result.Trend,
                SampleCount = result.SampleCount,
                MovingAverageLast = result.MovingAverageLast,
                AlertLevel = result.AlertLevel,
                CreatedAt = DateTime.UtcNow,
                RawGrpcResultSerialized = JsonFormatter.Default.Format(result)
            };

            using var cts = new CancellationTokenSource(_writeTimeout);
            await _analysesRepository.InsertAsync(analysis, cts.Token);
            Console.WriteLine($"[Servidor][MongoDB] Analise persistida em analyses: tipo={request.Type}, zona={request.Zone}, janela={windowStart:yyyy-MM-ddTHH:mm:ssZ}->{windowEnd:yyyy-MM-ddTHH:mm:ssZ}.");
            return analysis;
        }
        catch (OperationCanceledException ex)
        {
            Console.WriteLine($"[ERRO][MongoDB] Timeout ao persistir analise apos {_writeTimeout.TotalSeconds:0}s: {ex.Message}. Resultado mantido apenas em memoria/ficheiro local.");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO][MongoDB] Falha ao persistir analise no MongoDB: {ex.GetType().Name}: {ex.Message}. Resultado mantido apenas em memoria/ficheiro local.");
            return null;
        }
    }
}
