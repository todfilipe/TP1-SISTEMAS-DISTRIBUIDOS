using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Servidor.Mongo.Models;

namespace Servidor;

internal sealed class ServidorHttpApi : IDisposable
{
    private readonly ServidorTCP _servidor;
    private readonly int _port;
    private WebApplication? _app;
    private Thread? _thread;

    internal ServidorHttpApi(ServidorTCP servidor, int port)
    {
        _servidor = servidor;
        _port = port;
    }

    internal void Start()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "ServidorHttpApi"
        };
        _thread.Start();
    }

    private void Run()
    {
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://0.0.0.0:{_port}");
            var app = builder.Build();

            app.MapGet("/health", () => Results.Ok(new
            {
                status = "ok",
                service = "servidor",
                generatedAt = DateTime.UtcNow
            }));

            app.MapPost("/api/analyses", (AnalysisApiRequest request) =>
            {
                AnalysisExecutionResult? execution = _servidor.ExecutarAnaliseComPersistencia(
                    request.Type ?? "",
                    request.Zone ?? "",
                    request.SensorId ?? "",
                    request.From ?? "",
                    request.To ?? "");

                if (execution == null)
                {
                    return Results.Problem("Servico de analise do Servidor indisponivel.", statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                if (execution.Document == null)
                {
                    int status = IsMissingReadingsError(execution.ErrorMessage)
                        ? StatusCodes.Status404NotFound
                        : StatusCodes.Status502BadGateway;

                    return Results.Json(new { error = execution.ErrorMessage ?? execution.Result.ResultSummary }, statusCode: status);
                }

                return Results.Created("/api/analyses/" + execution.Document.Id, MapAnalysis(execution.Document));
            });

            _app = app;
            Console.WriteLine($"[Servidor][HTTP] API do Servidor disponivel em http://0.0.0.0:{_port}");
            app.Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Servidor][HTTP] API indisponivel: {ex.Message}");
        }
    }

    private static bool IsMissingReadingsError(string? message)
    {
        return !string.IsNullOrWhiteSpace(message)
            && message.Contains("nao existem leituras", StringComparison.OrdinalIgnoreCase);
    }

    private static object MapAnalysis(AnalysisDocument doc)
    {
        return new
        {
            id = doc.Id,
            zone = doc.Zone,
            type = doc.Type,
            sensorId = doc.SensorId,
            windowStart = ToIso(doc.WindowStart),
            windowEnd = ToIso(doc.WindowEnd),
            average = doc.Average,
            median = doc.Median,
            standardDeviation = doc.StandardDeviation,
            percentile25 = doc.Percentile25,
            percentile75 = doc.Percentile75,
            percentile95 = doc.Percentile95,
            min = doc.Min,
            max = doc.Max,
            outlierCount = doc.OutlierCount,
            trendSlope = doc.TrendSlope,
            trendClassification = doc.TrendClassification,
            sampleCount = doc.SampleCount,
            movingAverageLast = doc.MovingAverageLast,
            alertLevel = doc.AlertLevel,
            createdAt = ToIso(doc.CreatedAt)
        };
    }

    private static string ToIso(DateTime value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    public void Dispose()
    {
        try { _app?.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); } catch { }
    }
}

internal sealed class AnalysisApiRequest
{
    public string? Type { get; set; }
    public string? Zone { get; set; }
    public string? SensorId { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
}
