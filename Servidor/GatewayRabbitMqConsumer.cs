using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Servidor
{
    internal sealed class GatewayRabbitMqConsumer : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _userName;
        private readonly string _password;
        private readonly string _virtualHost;
        private readonly string _exchangeName;
        private readonly string _queueName;
        private readonly ServidorTCP _server;

        private IConnection _connection = null!;
        private IModel _channel = null!;

        internal GatewayRabbitMqConsumer(
            string host,
            int port,
            string userName,
            string password,
            string virtualHost,
            string exchangeName,
            string queueName,
            ServidorTCP server)
        {
            _host = host;
            _port = port;
            _userName = userName;
            _password = password;
            _virtualHost = virtualHost;
            _exchangeName = exchangeName;
            _queueName = queueName;
            _server = server;
        }

        internal void Start()
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

            string deadLetterExchange = $"{_exchangeName}.dlx";
            string deadLetterQueue = $"{_queueName}.dead";

            _channel.ExchangeDeclare(_exchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
            _channel.ExchangeDeclare(deadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false);
            _channel.QueueDeclare(deadLetterQueue, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(deadLetterQueue, deadLetterExchange, routingKey: "");

            var queueArgs = new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"] = deadLetterExchange
            };

            _channel.QueueDeclare(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: queueArgs);
            _channel.QueueBind(_queueName, _exchangeName, "gateway.#");
            _channel.BasicQos(prefetchSize: 0, prefetchCount: 20, global: false);

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += OnReceived;
            _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);

            Console.WriteLine($"[Servidor][RabbitMQ] A consumir Gateway->Servidor na fila '{_queueName}' (exchange '{_exchangeName}').");
        }

        private void OnReceived(object? sender, BasicDeliverEventArgs ea)
        {
            try
            {
                string payload = Encoding.UTF8.GetString(ea.Body.ToArray());
                GatewayServerEnvelope? envelope = JsonSerializer.Deserialize<GatewayServerEnvelope>(payload);

                if (envelope == null || string.IsNullOrWhiteSpace(envelope.GatewayId) || string.IsNullOrWhiteSpace(envelope.Message))
                {
                    Console.WriteLine("[Servidor][RabbitMQ] Envelope invalido recebido; enviado para dead-letter.");
                    _channel.BasicReject(ea.DeliveryTag, requeue: false);
                    return;
                }

                Console.WriteLine($"[Rabbit <- {envelope.GatewayId}] {envelope.Message}");
                string response = _server.ProcessarMensagemRabbit(envelope.GatewayId, envelope.Message, envelope.MessageId);
                Console.WriteLine($"[Rabbit -> {envelope.GatewayId}] {response}");

                if (ShouldAck(response))
                {
                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                    return;
                }

                if (ShouldDeadLetter(response))
                {
                    _channel.BasicReject(ea.DeliveryTag, requeue: false);
                    return;
                }

                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[Servidor][RabbitMQ] JSON invalido recebido; enviado para dead-letter: {ex.Message}");
                _channel.BasicReject(ea.DeliveryTag, requeue: false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Servidor][RabbitMQ] Falha ao processar mensagem; requeue=true: {ex.Message}");
                try { _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true); } catch { }
            }
        }

        private static bool ShouldAck(string response)
        {
            return response.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
                || string.Equals(response, "ERR_ALREADY_CONNECTED", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldDeadLetter(string response)
        {
            return string.Equals(response, "ERR_INVALID_DATA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(response, "ERR_SEQUENCE", StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
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
