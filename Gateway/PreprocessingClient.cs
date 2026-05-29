using System;
using Grpc.Core;
using Grpc.Net.Client;
using Polly;
using Polly.Retry;
using Preprocessing;

namespace Gateway
{
    internal sealed class PreprocessingClient
    {
        private readonly PreprocessingService.PreprocessingServiceClient _client;
        private readonly ResiliencePipeline<NormalizedReading> _pipeline;

        public PreprocessingClient(string url)
        {
            var channel = GrpcChannel.ForAddress(url);
            _client = new PreprocessingService.PreprocessingServiceClient(channel);
            _pipeline = new ResiliencePipelineBuilder<NormalizedReading>()
                .AddRetry(new RetryStrategyOptions<NormalizedReading>
                {
                    ShouldHandle = new PredicateBuilder<NormalizedReading>()
                        .Handle<RpcException>(ex => ex.StatusCode == StatusCode.Unavailable ||
                                                    ex.StatusCode == StatusCode.DeadlineExceeded ||
                                                    ex.StatusCode == StatusCode.Internal)
                        .Handle<Exception>(),
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(1)
                })
                .AddTimeout(TimeSpan.FromSeconds(5))
                .Build();

            Console.WriteLine($"[GATEWAY] Cliente gRPC de Pré-processamento inicializado para: {url}");
        }

        public NormalizedReading Normalize(string sensorId, string type, double value, string unit, string timestamp, string rawFormat, string zone)
        {
            var request = new RawReading
            {
                SensorId = sensorId,
                Type = type,
                Value = value,
                Unit = unit ?? "",
                Timestamp = timestamp,
                RawFormat = rawFormat ?? "",
                Zone = zone ?? ""
            };

            try
            {
                return _pipeline.Execute(() => _client.Normalize(request));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO gRPC] Falha catastrófica ao normalizar leitura do sensor {sensorId} após retentativas: {ex.Message}");
                return null!;
            }
        }
    }
}
