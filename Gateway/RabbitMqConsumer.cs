using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Sensor;

namespace Gateway
{
    internal sealed class RabbitMqConsumer : IDisposable
    {
        private readonly string _gatewayId;
        private readonly string _host;
        private readonly int _port;
        private readonly string _userName;
        private readonly string _password;
        private readonly string _virtualHost;
        private readonly string _exchangeName;
        private readonly IConfiguration _configuration;
        private readonly string[] _args;
        private readonly SensorConfigManager _configManager;
        private readonly Func<string, string> _sendToServer;
        private readonly ReadingAggregator _aggregator;
        private readonly PreprocessingClient _preprocessingClient;

        private IConnection _connection = null!;
        private IModel _channel = null!;

        public RabbitMqConsumer(
            string gatewayId,
            string host,
            int port,
            string userName,
            string password,
            string virtualHost,
            string exchangeName,
            IConfiguration configuration,
            string[] args,
            SensorConfigManager configManager,
            Func<string, string> sendToServer,
            ReadingAggregator aggregator,
            PreprocessingClient preprocessingClient)
        {
            _gatewayId = gatewayId;
            _host = host;
            _port = port;
            _userName = userName;
            _password = password;
            _virtualHost = virtualHost;
            _exchangeName = exchangeName;
            _configuration = configuration;
            _args = args;
            _configManager = configManager;
            _sendToServer = sendToServer;
            _aggregator = aggregator;
            _preprocessingClient = preprocessingClient;
        }

        public void Start()
        {
            var factory = new ConnectionFactory
            {
                HostName = _host,
                Port = _port,
                UserName = _userName,
                Password = _password,
                VirtualHost = _virtualHost
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            _channel.ExchangeDeclare(
                exchange: _exchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null
            );

            string queueName = $"gateway.{_gatewayId}";
            _channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            foreach (var binding in ResolveBindings())
            {
                _channel.QueueBind(queueName, _exchangeName, binding);
                Console.WriteLine($"[GATEWAY] Fila vinculada ao padrão: '{binding}' no exchange '{_exchangeName}'");
            }

            _channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += OnReceived;

            _channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
            Console.WriteLine($"[GATEWAY] A escutar mensagens do RabbitMQ na fila '{queueName}'...");
        }

        private List<string> ResolveBindings()
        {
            var bindings = new List<string>();
            if (_args.Length >= 5 && !int.TryParse(_args[4], out _))
            {
                bindings.AddRange(_args[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                return bindings;
            }

            var configBindings = _configuration.GetSection("Bindings").GetChildren().Select(c => c.Value).Where(v => v != null).ToList();
            if (configBindings.Count > 0)
            {
                bindings.AddRange(configBindings!);
                return bindings;
            }

            bindings.AddRange(
                _configManager.GetAllSensors()
                    .Select(s => s.Zona)
                    .Where(z => !string.IsNullOrEmpty(z))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(zone => $"{zone}.#"));

            return bindings;
        }

        private void OnReceived(object? model, BasicDeliverEventArgs ea)
        {
            ulong deliveryTag = ea.DeliveryTag;
            try
            {
                string message = Encoding.UTF8.GetString(ea.Body.ToArray());
                bool shouldAck = ProcessMessage(message);

                if (shouldAck)
                {
                    _channel.BasicAck(deliveryTag, multiple: false);
                    return;
                }

                _channel.BasicNack(deliveryTag, multiple: false, requeue: true);
                Console.WriteLine($"[RABBITMQ] Mensagem NACKed com requeue=true. DeliveryTag: {deliveryTag}");
                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO CONSUMO] Falha ao tratar callback do RabbitMQ: {ex.Message}");
                try
                {
                    _channel.BasicNack(deliveryTag, multiple: false, requeue: true);
                }
                catch (Exception nackEx)
                {
                    Console.WriteLine($"[ERRO NACK] Falha ao enviar NACK: {nackEx.Message}");
                }
            }
        }

        private bool ProcessMessage(string message)
        {
            try
            {
                SensorMessage? msg;
                try
                {
                    msg = JsonSerializer.Deserialize<SensorMessage>(message);
                }
                catch (JsonException jsonEx)
                {
                    Console.WriteLine($"[ERRO RABBITMQ] Falha de desserialização JSON (Poison Message): {jsonEx.Message}");
                    return true;
                }

                if (msg == null)
                {
                    Console.WriteLine("[ERRO RABBITMQ] Mensagem JSON nula (Poison Message).");
                    return true;
                }

                string sensorId = msg.sensorId;
                string type = msg.type;

                if (string.IsNullOrEmpty(sensorId))
                {
                    Console.WriteLine("[ERRO RABBITMQ] Mensagem sem sensorId (Poison Message).");
                    return true;
                }

                if (string.Equals(type, "HEARTBEAT", StringComparison.OrdinalIgnoreCase))
                {
                    return ProcessHeartbeat(sensorId);
                }

                if (string.Equals(type, "DISCONNECT", StringComparison.OrdinalIgnoreCase))
                {
                    return ProcessDisconnect(sensorId);
                }

                return ProcessReading(msg, sensorId, type);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO PROCESSAMENTO RABBIT] Falha inesperada ao processar mensagem: {ex.Message}");
                return false;
            }
        }

        private bool ProcessHeartbeat(string sensorId)
        {
            Console.WriteLine($"[RABBITMQ HEARTBEAT] Sensor '{sensorId}' heartbeat recebido.");
            var sensor = _configManager.GetSensor(sensorId);
            if (sensor != null)
            {
                if (sensor.Estado == "indisponivel" || sensor.Estado == "desligado")
                {
                    _configManager.ChangeSensorStatus(sensorId, "ativo");
                    string response = _sendToServer($"SENSOR_STATUS {sensorId} ativo");
                    if (response != null)
                    {
                        Console.WriteLine($"[GATEWAY] SENSOR_STATUS (ativo após inatividade/HEARTBEAT via RabbitMQ) enviado para '{sensorId}'. Resposta: {response}");
                    }
                }
                _configManager.UpdateLastSync(sensorId, DateTime.UtcNow);
            }
            return true;
        }

        private bool ProcessDisconnect(string sensorId)
        {
            Console.WriteLine($"[RABBITMQ DISCONNECT] Sensor '{sensorId}' solicitou desconexão.");
            _configManager.ChangeSensorStatus(sensorId, "desligado");
            string response = _sendToServer($"SENSOR_STATUS {sensorId} desligado");
            if (response != null)
            {
                Console.WriteLine($"[GATEWAY] SENSOR_STATUS enviado ao Servidor para '{sensorId}' (desconexão via RabbitMQ). Resposta: {response}");
            }
            return true;
        }

        private bool ProcessReading(SensorMessage msg, string sensorId, string type)
        {
            string validationRaw = BuildTp1RawMessage(msg);
            DataValidationResult validationResult =
                DataValidator.ValidateAndProcessData(validationRaw, sensorId, _configManager, new List<string>());

            Console.WriteLine(validationResult.LogMessage);
            if (!validationResult.IsValid)
            {
                Console.WriteLine($"[AVISO VALIDAÇÃO] Leitura do sensor '{sensorId}' rejeitada na validação (Poison Message): {validationResult.ErrorCode}");
                return true;
            }

            var sensor = _configManager.GetSensor(sensorId);
            if (sensor == null)
            {
                Console.WriteLine($"[AVISO VALIDAÇÃO] Sensor '{sensorId}' não encontrado após validação (Poison Message).");
                return true;
            }

            if (sensor.Estado == "indisponivel" || sensor.Estado == "desligado")
            {
                _configManager.ChangeSensorStatus(sensorId, "ativo");
                string response = _sendToServer($"SENSOR_STATUS {sensorId} ativo");
                if (response != null)
                {
                    Console.WriteLine($"[GATEWAY] SENSOR_STATUS (ativo após inatividade/DATA via RabbitMQ) enviado para '{sensorId}'. Resposta: {response}");
                }
            }

            double originalValue = msg.value;
            string readingZone = !string.IsNullOrEmpty(msg.zone) ? msg.zone : sensor.Zona;
            string payloadFormat = string.IsNullOrWhiteSpace(msg.rawFormat) ? "UNKNOWN" : msg.rawFormat.ToUpperInvariant();
            Console.WriteLine($"[GATEWAY gRPC] A normalizar payload rawFormat={payloadFormat} do sensor '{sensorId}'.");

            var normalized = _preprocessingClient.Normalize(sensorId, type, originalValue, msg.unit, msg.timestamp, msg.raw, readingZone);
            if (normalized == null)
            {
                Console.WriteLine($"[RABBITMQ NACK] Falha ao normalizar leitura do sensor '{sensorId}' (gRPC offline). Requeuing...");
                return false;
            }

            if (!normalized.IsValid)
            {
                Console.WriteLine($"[AVISO gRPC] Leitura do sensor '{sensorId}' rejeitada pelo gRPC Preprocessing (Poison/Invalid Message).");
                return true;
            }

            if (Math.Abs(originalValue - normalized.Value) > 0.0001)
            {
                Console.WriteLine($"[GATEWAY gRPC] Valor normalizado para '{sensorId}': {originalValue} -> {normalized.Value:F2} (unidade original convertida)");
            }

            string normalizedValueText = normalized.Value.ToString("F2", CultureInfo.InvariantCulture);
            string finalZone = !string.IsNullOrEmpty(normalized.Zone) ? normalized.Zone : sensor.Zona;
            string normalizedForward = $"FORWARD {sensorId} {normalized.Type} {normalizedValueText} {finalZone} {normalized.Timestamp}";

            _aggregator.Enqueue(normalizedForward);
            _configManager.UpdateLastSync(sensorId, DateTime.UtcNow);
            return true;
        }

        private static string BuildTp1RawMessage(SensorMessage msg)
        {
            string value = msg.value.ToString(CultureInfo.InvariantCulture);
            return $"DATA {msg.type} {value} {msg.zone} {msg.timestamp}";
        }

        public void Dispose()
        {
            try { _channel?.Close(); } catch { }
            try { _connection?.Close(); } catch { }
        }
    }
}
