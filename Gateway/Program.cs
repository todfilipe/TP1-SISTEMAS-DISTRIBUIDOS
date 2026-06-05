using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace Gateway
{
    class Program
    {
        private static volatile bool _running = true;
        private static readonly object ShutdownLock = new object();
        private static bool _shutdownStarted;

        private static SensorConfigManager _configManager = null!;
        private static ServerConnection? _serverConnection;
        private static GatewayMessagePublisher? _gatewayPublisher;
        private static RetryBuffer _retryBuffer = null!;
        private static HeartbeatGateway _heartbeatMonitor = null!;
        private static ReadingAggregator _readingAggregator = null!;
        private static VideoStreamHandler _videoStreamHandler = null!;
        private static RabbitMqConsumer _rabbitConsumer = null!;

        static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            string gatewayId = Environment.GetEnvironmentVariable("GATEWAY_ID") ?? config["Gateway:Id"] ?? "GW1";
            string serverIp = Environment.GetEnvironmentVariable("SERVER_HOST") ?? config["Gateway:ServerIp"] ?? "127.0.0.1";
            int serverPort = int.TryParse(config["Gateway:ServerPort"], out int spVal) ? spVal : 9090;
            int videoPort = int.TryParse(config["Gateway:VideoPort"], out int vpVal) ? vpVal : 8081;
            string preprocessingUrl =
                Environment.GetEnvironmentVariable("PREPROCESSING_SERVICE_URL") ??
                config["Gateway:PreprocessingUrl"] ??
                "http://localhost:50051";
            string serverTransport =
                Environment.GetEnvironmentVariable("GATEWAY_SERVER_TRANSPORT") ??
                config["Gateway:ServerTransport"] ??
                "rabbit";

            string rabbitHost = Environment.GetEnvironmentVariable("RABBIT_HOST") ?? config["RabbitMQ:Host"] ?? "localhost";
            int rabbitPort = int.TryParse(config["RabbitMQ:Port"], out int rpVal) ? rpVal : 5672;
            rabbitPort = int.TryParse(Environment.GetEnvironmentVariable("RABBIT_PORT"), out int envRabbitPort) ? envRabbitPort : rabbitPort;
            string rabbitUserName = Environment.GetEnvironmentVariable("RABBIT_USER") ?? config["RabbitMQ:UserName"] ?? "admin";
            string rabbitPassword = Environment.GetEnvironmentVariable("RABBIT_PASS") ?? config["RabbitMQ:Password"] ?? "admin";
            string rabbitVirtualHost = Environment.GetEnvironmentVariable("RABBIT_VHOST") ?? config["RabbitMQ:VirtualHost"] ?? "onehealth";
            string rabbitExchangeName = Environment.GetEnvironmentVariable("RABBIT_EXCHANGE") ?? config["RabbitMQ:ExchangeName"] ?? "sensors.exchange";
            string rabbitGatewayExchangeName =
                Environment.GetEnvironmentVariable("RABBIT_GATEWAY_EXCHANGE") ??
                config["RabbitMQ:GatewayExchangeName"] ??
                "gateway.exchange";

            if (args.Length >= 1) gatewayId = args[0];
            if (args.Length >= 2) serverIp = args[1];
            if (args.Length >= 3 && int.TryParse(args[2], out int parsedServerPort)) serverPort = parsedServerPort;
            if (args.Length >= 4 && int.TryParse(args[3], out int parsedVideoPort)) videoPort = parsedVideoPort;

            Console.WriteLine($"[GATEWAY] A iniciar com ID: {gatewayId}");
            Console.WriteLine($"[CONFIG] Servidor Central: {serverIp}:{serverPort}");
            Console.WriteLine($"[CONFIG] Video Port: {videoPort}");
            Console.WriteLine($"[CONFIG] Preprocessing URL: {preprocessingUrl}");
            Console.WriteLine($"[CONFIG] Transporte Gateway->Servidor: {serverTransport}");
            Console.WriteLine($"[CONFIG] RabbitMQ: host={rabbitHost}:{rabbitPort}, user={rabbitUserName}, vhost={rabbitVirtualHost}, exchange={rabbitExchangeName}");
            Console.WriteLine($"[CONFIG] RabbitMQ Gateway->Servidor: exchange={rabbitGatewayExchangeName}");

            try
            {
                string csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensors.csv");
                if (!File.Exists(csvPath))
                {
                    csvPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\sensors.csv"));
                }
                _configManager = new SensorConfigManager(csvPath);

                var preprocessingClient = new PreprocessingClient(preprocessingUrl);

                Func<string, string> sendToServer;
                if (string.Equals(serverTransport, "tcp", StringComparison.OrdinalIgnoreCase))
                {
                    _serverConnection = new ServerConnection(gatewayId, serverIp, serverPort, _configManager);
                    if (!_serverConnection.Connect())
                    {
                        return;
                    }
                    sendToServer = _serverConnection.Send;
                }
                else
                {
                    _gatewayPublisher = new GatewayMessagePublisher(
                        gatewayId,
                        rabbitHost,
                        rabbitPort,
                        rabbitUserName,
                        rabbitPassword,
                        rabbitVirtualHost,
                        rabbitGatewayExchangeName);
                    if (!_gatewayPublisher.Connect())
                    {
                        return;
                    }
                    sendToServer = _gatewayPublisher.Send;
                }

                int loaded = _configManager.LoadConfig();
                Console.WriteLine($"[GATEWAY] Configuração de sensores carregada ({loaded} sensor(es)).");

                _retryBuffer = new RetryBuffer(sendToServer);
                _retryBuffer.Start();

                _heartbeatMonitor = new HeartbeatGateway(_configManager, sendToServer);
                _heartbeatMonitor.Start();

                _readingAggregator = new ReadingAggregator(sendToServer, _retryBuffer);
                _readingAggregator.Start();

                _videoStreamHandler = new VideoStreamHandler(videoPort, _configManager, sendToServer, _retryBuffer);
                _videoStreamHandler.Start();

                _rabbitConsumer = new RabbitMqConsumer(
                    gatewayId,
                    rabbitHost,
                    rabbitPort,
                    rabbitUserName,
                    rabbitPassword,
                    rabbitVirtualHost,
                    rabbitExchangeName,
                    config,
                    args,
                    _configManager,
                    sendToServer,
                    _readingAggregator,
                    preprocessingClient);
                _rabbitConsumer.Start();

                RegisterShutdownHandlers(gatewayId);

                while (_running)
                {
                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Falha no arranque/execução do Gateway: {ex.Message}");
            }
            finally
            {
                Shutdown(gatewayId, notifyServer: true);
            }
        }

        private static void RegisterShutdownHandlers(string gatewayId)
        {
            Console.CancelKeyPress += (sender, e) =>
            {
                Console.WriteLine("\n[GATEWAY] A encerrar Gateway...");
                e.Cancel = true;
                _running = false;
                Shutdown(gatewayId, notifyServer: true);
            };

            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                Shutdown(gatewayId, notifyServer: true);
            };
        }

        private static void Shutdown(string gatewayId, bool notifyServer)
        {
            lock (ShutdownLock)
            {
                if (_shutdownStarted)
                {
                    return;
                }
                _shutdownStarted = true;
            }

            _running = false;

            try { _rabbitConsumer?.Dispose(); } catch { }
            try { _videoStreamHandler?.Dispose(); } catch { }
            try { _readingAggregator?.Stop(); } catch { }
            try { _retryBuffer?.Stop(); } catch { }
            try { _heartbeatMonitor?.Stop(); } catch { }

            if (notifyServer)
            {
                try { _serverConnection?.Disconnect(); } catch { }
                try { _gatewayPublisher?.Disconnect(); } catch { }
            }

            try { _serverConnection?.Dispose(); } catch { }
            try { _gatewayPublisher?.Dispose(); } catch { }
            try { _configManager?.Dispose(); } catch { }
        }
    }
}
