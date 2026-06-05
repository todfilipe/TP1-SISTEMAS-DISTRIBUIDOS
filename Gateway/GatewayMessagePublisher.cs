using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace Gateway
{
    internal sealed class GatewayMessagePublisher : IDisposable
    {
        private readonly string _gatewayId;
        private readonly string _host;
        private readonly int _port;
        private readonly string _userName;
        private readonly string _password;
        private readonly string _virtualHost;
        private readonly string _exchangeName;
        private readonly object _lock = new object();

        private IConnection _connection = null!;
        private IModel _channel = null!;
        private volatile bool _connected;

        public GatewayMessagePublisher(
            string gatewayId,
            string host,
            int port,
            string userName,
            string password,
            string virtualHost,
            string exchangeName)
        {
            _gatewayId = gatewayId;
            _host = host;
            _port = port;
            _userName = userName;
            _password = password;
            _virtualHost = virtualHost;
            _exchangeName = exchangeName;
        }

        public bool Connect()
        {
            lock (_lock)
            {
                try
                {
                    var factory = new ConnectionFactory
                    {
                        HostName = _host,
                        Port = _port,
                        UserName = _userName,
                        Password = _password,
                        VirtualHost = _virtualHost,
                        AutomaticRecoveryEnabled = true,
                        NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
                    };

                    _connection = factory.CreateConnection();
                    _channel = _connection.CreateModel();
                    _channel.ExchangeDeclare(
                        exchange: _exchangeName,
                        type: ExchangeType.Topic,
                        durable: true,
                        autoDelete: false,
                        arguments: null);
                    _channel.ConfirmSelect();
                    _connected = true;

                    Console.WriteLine($"[GATEWAY] Publisher RabbitMQ pronto: exchange='{_exchangeName}', gateway='{_gatewayId}'.");
                    Send($"GW_CONNECT {_gatewayId}");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO] Nao foi possivel iniciar publisher RabbitMQ ({_host}:{_port}): {ex.Message}");
                    _connected = false;
                    return false;
                }
            }
        }

        public string Send(string message)
        {
            lock (_lock)
            {
                if (!_connected)
                {
                    return null!;
                }

                try
                {
                    string messageId = Guid.NewGuid().ToString("N");
                    var envelope = new GatewayServerEnvelope
                    {
                        GatewayId = _gatewayId,
                        Message = message,
                        MessageId = messageId,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
                    var properties = _channel.CreateBasicProperties();
                    properties.Persistent = true;
                    properties.MessageId = messageId;
                    properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    properties.ContentType = "application/json";

                    string routingKey = $"gateway.{_gatewayId}.{ResolveCommand(message)}";
                    _channel.BasicPublish(
                        exchange: _exchangeName,
                        routingKey: routingKey,
                        mandatory: false,
                        basicProperties: properties,
                        body: body);

                    if (!_channel.WaitForConfirms(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("RabbitMQ nao confirmou a publicacao.");
                    }

                    return BuildSyntheticResponse(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO] Falha ao publicar mensagem Gateway->Servidor via RabbitMQ: {ex.Message}");
                    return null!;
                }
            }
        }

        public void Disconnect()
        {
            try
            {
                Send($"GW_DISCONNECT {_gatewayId}");
                Console.WriteLine("[GATEWAY] GW_DISCONNECT publicado no RabbitMQ.");
            }
            catch
            {
                // Broker pode ja estar indisponivel no encerramento.
            }
        }

        private static string ResolveCommand(string message)
        {
            string command = message.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? "UNKNOWN";
            return command.ToLowerInvariant();
        }

        private static string BuildSyntheticResponse(string message)
        {
            string command = ResolveCommand(message).ToUpperInvariant();
            return command switch
            {
                "GW_CONNECT" => "OK_GW_CONNECTED",
                "GW_DISCONNECT" => "OK_GW_DISCONNECT",
                "SENSOR_STATUS" => "OK_STATUS_RECEIVED",
                _ => "OK"
            };
        }

        public void Dispose()
        {
            _connected = false;
            try { _channel?.Close(); } catch { }
            try { _connection?.Close(); } catch { }
        }
    }

    internal sealed class GatewayServerEnvelope
    {
        public string GatewayId { get; set; } = "";
        public string Message { get; set; } = "";
        public string MessageId { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
    }
}
