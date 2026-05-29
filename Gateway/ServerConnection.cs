using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Gateway
{
    internal sealed class ServerConnection : IDisposable
    {
        private readonly string _gatewayId;
        private readonly string _host;
        private readonly int _port;
        private readonly SensorConfigManager _configManager;
        private readonly object _lock = new object();

        private TcpClient _client = null!;
        private StreamReader _reader = null!;
        private StreamWriter _writer = null!;
        private bool _isReconnecting;
        private volatile bool _running = true;

        public ServerConnection(string gatewayId, string host, int port, SensorConfigManager configManager)
        {
            _gatewayId = gatewayId;
            _host = host;
            _port = port;
            _configManager = configManager;
        }

        public bool Connect()
        {
            try
            {
                var client = new TcpClient(_host, _port);
                var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.UTF8);
                var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };

                writer.WriteLine($"GW_CONNECT {_gatewayId}");
                Console.WriteLine($"[GATEWAY] Pedido de ligação enviado ao Servidor (GW_CONNECT {_gatewayId}).");

                string? response = reader.ReadLine();
                if (response == null || !response.StartsWith("OK_GW_CONNECTED"))
                {
                    Console.WriteLine($"[ERRO] Falha ao ligar ao Servidor. Resposta obtida: {response}");
                    client.Close();
                    return false;
                }

                lock (_lock)
                {
                    _client = client;
                    _reader = reader;
                    _writer = writer;
                    _isReconnecting = false;
                }

                Console.WriteLine("[GATEWAY] Ligado com sucesso ao Servidor (recebido OK_GW_CONNECTED).");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Não foi possível ligar ao Servidor ({_host}:{_port}): {ex.Message}");
                return false;
            }
        }

        public string Send(string message)
        {
            lock (_lock)
            {
                if (_isReconnecting)
                {
                    return null!;
                }

                try
                {
                    if (_client == null || !_client.Connected)
                    {
                        throw new IOException("Socket fechada.");
                    }

                    _writer.WriteLine(message);
                    string? response = _reader.ReadLine();
                    if (response == null)
                    {
                        throw new IOException("Conexão fechada prematuramente pelo Servidor (EOF).");
                    }

                    return response;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO] Falha na comunicação com o Servidor: {ex.Message}");
                    StartReconnect();
                    return null!;
                }
            }
        }

        public void Disconnect()
        {
            lock (_lock)
            {
                try
                {
                    _writer?.WriteLine($"GW_DISCONNECT {_gatewayId}");
                    Console.WriteLine("[GATEWAY] GW_DISCONNECT enviado ao Servidor no encerramento.");
                }
                catch
                {
                    // Servidor pode já estar desligado.
                }
            }
        }

        private void StartReconnect()
        {
            if (_isReconnecting)
            {
                return;
            }

            _isReconnecting = true;
            var reconnectThread = new Thread(ReconnectLoop)
            {
                IsBackground = true,
                Name = "ServerReconnect"
            };
            reconnectThread.Start();
        }

        private void ReconnectLoop()
        {
            lock (_lock)
            {
                if (!_isReconnecting)
                {
                    return;
                }

                _reader?.Close();
                _writer?.Close();
                _client?.Close();
            }

            while (_running)
            {
                Console.WriteLine($"[GATEWAY] A tentar reconectar ao Servidor em {_host}:{_port}...");
                try
                {
                    var newClient = new TcpClient(_host, _port);
                    var stream = newClient.GetStream();
                    var newReader = new StreamReader(stream, Encoding.UTF8);
                    var newWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };

                    newWriter.WriteLine($"GW_CONNECT {_gatewayId}");
                    string? response = newReader.ReadLine();

                    if (response != null && response.StartsWith("OK_GW_CONNECTED"))
                    {
                        Console.WriteLine("[GATEWAY] Reconectado com sucesso ao Servidor Central.");

                        lock (_lock)
                        {
                            _client = newClient;
                            _reader = newReader;
                            _writer = newWriter;
                            ResyncActiveSensors();
                            _isReconnecting = false;
                        }
                        return;
                    }

                    newClient.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO] Falha na reconexão: {ex.Message}");
                }

                Thread.Sleep(5000);
            }
        }

        private void ResyncActiveSensors()
        {
            var sensors = _configManager?.GetDicionarioParaIteracao();
            if (sensors == null)
            {
                return;
            }

            foreach (var kvp in sensors)
            {
                string estado = kvp.Value.Estado;
                if (string.Equals(estado, "ativo", StringComparison.OrdinalIgnoreCase))
                {
                    _writer.WriteLine($"SENSOR_STATUS {kvp.Key} {estado}");
                    _reader.ReadLine();
                    Thread.Sleep(20);
                }
            }
        }

        public void Dispose()
        {
            _running = false;
            _reader?.Close();
            _writer?.Close();
            _client?.Close();
        }
    }
}
