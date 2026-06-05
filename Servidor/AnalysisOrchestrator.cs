using Analysis;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Retry;
using Servidor.Mongo.Models;
using Servidor.Mongo.Repositories;
using System.Globalization;

namespace Servidor;

internal sealed class AnalysisOrchestrator
{
    private readonly ReadingsRepository? _readingsRepository;
    private readonly MongoReadingPersister _mongoPersister;
    private readonly string _analysisServiceUrl;
    private readonly List<AnalysisResult> _historicoAnalises = new();
    private readonly List<PredictionResult> _historicoPrevisoes = new();
    private readonly object _historicoLock = new();

    private GrpcChannel? _analysisChannel;
    private AnalysisService.AnalysisServiceClient? _analysisClient;

    private readonly ResiliencePipeline _grpcPipeline =
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<RpcException>(ex => ex.StatusCode == StatusCode.Unavailable ||
                                                ex.StatusCode == StatusCode.DeadlineExceeded ||
                                                ex.StatusCode == StatusCode.Internal)
                    .Handle<Exception>(),
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = false,
                Delay = TimeSpan.FromSeconds(1),
                OnRetry = args =>
                {
                    Console.WriteLine($"[Servidor][Polly] Tentativa {args.AttemptNumber + 1} falhou. " +
                                      $"Nova tentativa em {args.RetryDelay.TotalSeconds}s. Erro: {args.Outcome.Exception?.Message}");
                    return default;
                }
            })
            .AddTimeout(TimeSpan.FromSeconds(10))
            .Build();

    internal AnalysisOrchestrator(ReadingsRepository? readingsRepository, MongoReadingPersister mongoPersister)
    {
        _readingsRepository = readingsRepository;
        _mongoPersister = mongoPersister;
        _analysisServiceUrl = CarregarAnalysisServiceUrl();
    }

    internal void Initialize()
    {
        try
        {
            _analysisChannel = GrpcChannel.ForAddress(_analysisServiceUrl);
            _analysisClient = new AnalysisService.AnalysisServiceClient(_analysisChannel);
            Console.WriteLine($"[Servidor] Cliente gRPC de Analise inicializado para: {_analysisServiceUrl}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO] Falha ao inicializar o cliente gRPC de Analise: {ex.Message}");
        }
    }

    internal (IReadOnlyList<AnalysisResult> Analises, IReadOnlyList<PredictionResult> Previsoes) ObterHistorico()
    {
        lock (_historicoLock)
        {
            return (_historicoAnalises.ToList(), _historicoPrevisoes.ToList());
        }
    }

    internal AnalysisResult? ExecutarAnalise(string tipo, string zona, string sensorId, string dateFrom, string dateTo)
    {
        return ExecutarAnaliseComPersistencia(tipo, zona, sensorId, dateFrom, dateTo)?.Result;
    }

    internal AnalysisExecutionResult? ExecutarAnaliseComPersistencia(string tipo, string zona, string sensorId, string dateFrom, string dateTo)
    {
        if (_analysisClient == null)
        {
            Console.WriteLine("[ERRO] Cliente gRPC de Analise nao esta inicializado.");
            return null;
        }

        var request = new AnalysisRequest
        {
            Type = tipo ?? "",
            Zone = zona ?? "",
            SensorId = sensorId ?? "",
            DateFrom = dateFrom ?? "",
            DateTo = dateTo ?? ""
        };

        var readings = ObterReadingsMongoParaGrpc(tipo, zona, sensorId, dateFrom, dateTo, "analise", out string? erroMongo);
        if (erroMongo != null)
        {
            var falhaMongo = CriarFalhaAnalise(erroMongo);
            GuardarResultadoAnalise(falhaMongo, request);
            return new AnalysisExecutionResult(falhaMongo, null, erroMongo);
        }

        foreach (var reading in readings)
        {
            request.Readings.Add(reading);
        }

        Console.WriteLine($"[Servidor] {request.Readings.Count} leitura(s) recolhida(s) do MongoDB/readings para analise.");
        Console.WriteLine("[Servidor] A enviar pedido de analise ao AnalysisService gRPC (com resiliencia Polly)...");

        try
        {
            AnalysisResult response = _grpcPipeline.Execute(() => _analysisClient.Analyze(request));
            GuardarResultadoAnalise(response, request);
            AnalysisDocument? document = _mongoPersister.PersistirAnaliseMongoAsync(response, request).GetAwaiter().GetResult();
            return new AnalysisExecutionResult(response, document, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO gRPC] Falha ao executar analise via gRPC apos retentativas: {ex.Message}");
            var falha = CriarFalhaAnalise($"FALHA: servico de Analise indisponivel apos 3 tentativas ({ex.Message}).");
            GuardarResultadoAnalise(falha, request);
            return new AnalysisExecutionResult(falha, null, falha.ResultSummary);
        }
    }

    internal PredictionResult? ExecutarPrevisao(string tipo, string zona, int periodos, string strategy = "linear")
    {
        if (_analysisClient == null)
        {
            Console.WriteLine("[ERRO] Cliente gRPC de Analise nao esta inicializado.");
            return null;
        }

        var request = new PredictionRequest
        {
            Type = tipo ?? "",
            Zone = zona ?? "",
            PeriodsToPredict = periodos,
            Strategy = strategy
        };

        var readings = ObterReadingsMongoParaGrpc(tipo, zona, null, null, null, "previsao", out string? erroMongo);
        if (erroMongo != null)
        {
            var falhaMongo = CriarFalhaPrevisao(erroMongo, request.Strategy);
            GuardarResultadoPrevisao(falhaMongo, request);
            return falhaMongo;
        }

        foreach (var reading in readings)
        {
            request.Readings.Add(reading);
        }

        Console.WriteLine($"[Servidor] {request.Readings.Count} leitura(s) historica(s) recolhida(s) do MongoDB/readings para previsao.");
        Console.WriteLine("[Servidor] A enviar pedido de previsao ao AnalysisService gRPC (com resiliencia Polly)...");

        try
        {
            PredictionResult response = _grpcPipeline.Execute(() => _analysisClient.Predict(request));
            GuardarResultadoPrevisao(response, request);
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO gRPC] Falha ao executar previsao via gRPC apos retentativas: {ex.Message}");
            Console.WriteLine($"  Sumario: FALHA: servico de Analise indisponivel apos 3 tentativas ({ex.Message}).");

            var falha = CriarFalhaPrevisao(
                $"FALHA: servico de Analise indisponivel apos 3 tentativas ({ex.Message}).",
                request.Strategy);
            GuardarResultadoPrevisao(falha, request);
            return falha;
        }
    }

    private IReadOnlyList<Reading> ObterReadingsMongoParaGrpc(
        string? tipo,
        string? zona,
        string? sensorId,
        string? dateFrom,
        string? dateTo,
        string operacao,
        out string? erro)
    {
        erro = null;

        if (_readingsRepository == null)
        {
            erro = $"FALHA: repositorio MongoDB de leituras indisponivel; nao foi possivel carregar dados da colecao readings para {operacao}. SQLite nao foi usado como fonte principal.";
            Console.WriteLine($"[AVISO][MongoDB] {erro}");
            return Array.Empty<Reading>();
        }

        string? normalizedType = ProtocolHelpers.NormalizarFiltroOpcional(tipo);
        string? normalizedZone = ProtocolHelpers.NormalizarFiltroOpcional(zona);
        string? normalizedSensorId = ProtocolHelpers.NormalizarFiltroOpcional(sensorId);

        if (!ProtocolHelpers.TryParseFiltroTemporal(dateFrom, out DateTime? from, out string? erroFrom))
        {
            erro = $"FALHA: filtro temporal inicial invalido para consulta MongoDB/readings: '{dateFrom}'.";
            Console.WriteLine($"[AVISO][MongoDB] {erroFrom}");
            return Array.Empty<Reading>();
        }

        if (!ProtocolHelpers.TryParseFiltroTemporal(dateTo, out DateTime? to, out string? erroTo))
        {
            erro = $"FALHA: filtro temporal final invalido para consulta MongoDB/readings: '{dateTo}'.";
            Console.WriteLine($"[AVISO][MongoDB] {erroTo}");
            return Array.Empty<Reading>();
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var documentos = _readingsRepository.FindAsync(
                sensorId: normalizedSensorId,
                zone: normalizedZone,
                type: normalizedType,
                from: from,
                to: to,
                cancellationToken: cts.Token).GetAwaiter().GetResult();

            if (documentos.Count == 0)
            {
                erro = $"FALHA: nao existem leituras na colecao MongoDB/readings para {operacao} com os filtros {ProtocolHelpers.DescreverFiltros(normalizedType, normalizedZone, normalizedSensorId, from, to)}. SQLite nao foi consultado.";
                Console.WriteLine($"[AVISO][MongoDB] {erro}");
                return Array.Empty<Reading>();
            }

            return documentos
                .OrderBy(reading => reading.Timestamp)
                .Select(MapearReadingMongoParaGrpc)
                .ToList();
        }
        catch (Exception ex)
        {
            erro = $"FALHA: MongoDB inacessivel ao consultar a colecao readings para {operacao}: {ex.Message}. SQLite nao foi consultado.";
            Console.WriteLine($"[AVISO][MongoDB] {erro}");
            return Array.Empty<Reading>();
        }
    }

    private static Reading MapearReadingMongoParaGrpc(ReadingDocument reading)
    {
        return new Reading
        {
            Value = reading.Value,
            Timestamp = reading.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            Type = reading.Type ?? "",
            SensorId = reading.SensorId ?? "",
            Zone = reading.Zone ?? ""
        };
    }

    private static AnalysisResult CriarFalhaAnalise(string mensagem)
    {
        return new AnalysisResult
        {
            ResultSummary = mensagem,
            ComputedAverage = 0.0,
            AlertLevel = "UNKNOWN",
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            SampleCount = 0
        };
    }

    private static PredictionResult CriarFalhaPrevisao(string mensagem, string strategy)
    {
        return new PredictionResult
        {
            PredictionSummary = mensagem,
            StrategyUsed = strategy,
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
    }

    private void GuardarResultadoAnalise(AnalysisResult result, AnalysisRequest request)
    {
        lock (_historicoLock)
        {
            _historicoAnalises.Add(result);
        }

        try
        {
            string logFile = "analysis_history.log";
            using (var writer = new StreamWriter(logFile, append: true, System.Text.Encoding.UTF8))
            {
                writer.WriteLine($"=== ANALISE EFECTUADA EM {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC ===");
                writer.WriteLine($"Request - Tipo: {request.Type}, Zona: {request.Zone}, Sensor: {request.SensorId}, From: {request.DateFrom}, To: {request.DateTo}");
                writer.WriteLine($"Result  - Summary: {result.ResultSummary}");
                writer.WriteLine($"Result  - Average: {result.ComputedAverage:F2}");
                writer.WriteLine($"Result  - Median: {result.Median:F2}");
                writer.WriteLine($"Result  - Percentiles: P25={result.Percentile25:F2}, P75={result.Percentile75:F2}, P95={result.Percentile95:F2}");
                writer.WriteLine($"Result  - Alert Level: {result.AlertLevel}");
                writer.WriteLine($"Result  - Timestamp: {result.Timestamp}");
                writer.WriteLine("==================================================");
                writer.WriteLine();
            }
            Console.WriteLine($"[Servidor] Resultado da analise guardado em '{logFile}' e em memoria.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO] Falha ao guardar resultado da analise no ficheiro: {ex.Message}");
        }
    }

    private void GuardarResultadoPrevisao(PredictionResult result, PredictionRequest request)
    {
        lock (_historicoLock)
        {
            _historicoPrevisoes.Add(result);
        }

        try
        {
            string logFile = "prediction_history.log";
            using (var writer = new StreamWriter(logFile, append: true, System.Text.Encoding.UTF8))
            {
                writer.WriteLine($"=== PREVISAO EFECTUADA EM {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC ===");
                writer.WriteLine($"Request - Tipo: {request.Type}, Zona: {request.Zone}, Periodos: {request.PeriodsToPredict}, Estrategia: {request.Strategy}");
                writer.WriteLine($"Result  - Summary: {result.PredictionSummary}");
                writer.WriteLine($"Result  - Strategy Used: {result.StrategyUsed}");
                writer.WriteLine($"Result  - Forecast: {string.Join(", ", result.Forecast)}");
                writer.WriteLine($"Result  - Timestamp: {result.Timestamp}");
                writer.WriteLine("==================================================");
                writer.WriteLine();
            }
            Console.WriteLine($"[Servidor] Resultado da previsao guardado em '{logFile}' e em memoria.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO] Falha ao guardar resultado da previsao no ficheiro: {ex.Message}");
        }
    }

    private static string CarregarAnalysisServiceUrl()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        return Environment.GetEnvironmentVariable("ANALYSIS_SERVICE_URL")
            ?? config["AnalysisService:Url"]
            ?? "http://localhost:50052";
    }
}

internal sealed record AnalysisExecutionResult(
    AnalysisResult Result,
    AnalysisDocument? Document,
    string? ErrorMessage);
