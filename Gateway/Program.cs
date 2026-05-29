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
        private static ServerConnection _serverConnection = null!;
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

            string gatewayId = config["Gateway:Id"] ?? "GW1";
            string serverIp = config["Gateway:ServerIp"] ?? "127.0.0.1";
            int serverPort = int.TryParse(config["Gateway:ServerPort"], out int spVal) ? spVal : 9090;
            int videoPort = int.TryParse(config["Gateway:VideoPort"], out int vpVal) ? vpVal : 8081;
            string preprocessingUrl =
                config["Gateway:PreprocessingUrl"] ??
                Environment.GetEnvironmentVariable("PREPROCESSING_SERVICE_URL") ??
                "http://localhost:50051";

            string rabbitHost = config["RabbitMQ:Host"] ?? "localhost";
            int rabbitPort = int.TryParse(config["RabbitMQ:Port"], out int rpVal) ? rpVal : 5672;
            string rabbitUserName = config["RabbitMQ:UserName"] ?? "admin";
            string rabbitPassword = config["RabbitMQ:Password"] ?? "admin";
            string rabbitVirtualHost = config["RabbitMQ:VirtualHost"] ?? "onehealth";
            string rabbitExchangeName = config["RabbitMQ:ExchangeName"] ?? "sensors.exchange";

            if (args.Length >= 1) gatewayId = args[0];
            if (args.Length >= 2) serverIp = args[1];
            if (args.Length >= 3 && int.TryParse(args[2], out int parsedServerPort)) serverPort = parsedServerPort;
            if (args.Length >= 4 && int.TryParse(args[3], out int parsedVideoPort)) videoPort = parsedVideoPort;

            Console.WriteLine($"[GATEWAY] A iniciar com ID: {gatewayId}");
            Console.WriteLine($"[CONFIG] Servidor Central: {serverIp}:{serverPort}");
            Console.WriteLine($"[CONFIG] Video Port: {videoPort}");
            Console.WriteLine($"[CONFIG] Preprocessing URL: {preprocessingUrl}");
            Console.WriteLine($"[CONFIG] RabbitMQ: host={rabbitHost}:{rabbitPort}, user={rabbitUserName}, vhost={rabbitVirtualHost}, exchange={rabbitExchangeName}");

            try
            {
                string csvPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\sensors.csv"));
                _configManager = new SensorConfigManager(csvPath);

                var preprocessingClient = new PreprocessingClient(preprocessingUrl);

                _serverConnection = new ServerConnection(gatewayId, serverIp, serverPort, _configManager);
                if (!_serverConnection.Connect())
                {
                    return;
                }

                int loaded = _configManager.LoadConfig();
                Console.WriteLine($"[GATEWAY] Configuração de sensores carregada ({loaded} sensor(es)).");

                _retryBuffer = new RetryBuffer(_serverConnection.Send);
                _retryBuffer.Start();

                _heartbeatMonitor = new HeartbeatGateway(_configManager, _serverConnection.Send);
                _heartbeatMonitor.Start();

                _readingAggregator = new ReadingAggregator(_serverConnection.Send, _retryBuffer);
                _readingAggregator.Start();

                _videoStreamHandler = new VideoStreamHandler(videoPort, _configManager, _serverConnection.Send, _retryBuffer);
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
                    _serverConnection.Send,
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
            }

            try { _serverConnection?.Dispose(); } catch { }
            try { _configManager?.Dispose(); } catch { }
        }
    }
}
