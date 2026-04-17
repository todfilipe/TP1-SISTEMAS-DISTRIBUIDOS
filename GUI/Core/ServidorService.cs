using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using OneHealth.Shared;

namespace OneHealthMonitor.Core
{
    /// <summary>
    /// Servidor TCP in-process que aguarda ligações de Gateways.
    /// Encapsula a lógica de Servidor/Servidor.cs + DataStore.
    /// </summary>
    public class ServidorService
    {
        private readonly DataStore _dataStore;
        private TcpListener? _listener;
        private volatile bool _running;
        private int _porta;
        private Thread? _listenThread;

        private readonly HashSet<string> _gatewaysLigados = new();
        private readonly object _gwListLock = new();

        private static readonly HashSet<string> EstadosValidos = new()
        {
            "ativo", "manutencao", "desativado", "indisponivel", "desligado"
        };

        // Contadores
        private int _totalMessages;
        private readonly Dictionary<string, int> _gwMessageCounts = new();
        private readonly Dictionary<string, (string Endpoint, DateTime Since)> _gwInfo = new();

        public event Action<string>? OnLogMessage;
        public event Action<string, string, string, string, string>? OnDataStored; // sensorId, tipo, valor, zona, ts

        public bool IsRunning => _running;
        public int Porta => _porta;
        public int TotalMessages => _totalMessages;
        public DataStore Store => _dataStore;

        public ServidorService(string? dataDirectory = null)
        {
            _dataStore = new DataStore(dataDirectory);
            _dataStore.OnLogMessage += msg => Log(msg);
        }

        private void Log(string msg) => OnLogMessage?.Invoke(msg);

        public void Start(int porta = 9090)
        {
            if (_running) return;

            _porta = porta;
            _running = true;

            _listenThread = new Thread(() => RunListener())
            {
                IsBackground = true,
                Name = "ServidorListener"
            };
            _listenThread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            Log("[Servidor] Parado.");
        }

        private void RunListener()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, _porta);
                _listener.Start();
                Log($"[Servidor] A escutar na porta {_porta}...");

                while (_running)
                {
                    if (_listener.Pending())
                    {
                        TcpClient client = _listener.AcceptTcpClient();
                        string endpoint = client.Client.RemoteEndPoint?.ToString() ?? "desconhecido";
                        Log($"[Servidor] Nova ligação TCP de: {endpoint}");

                        Thread clientThread = new Thread(() => TratarGateway(client, endpoint))
                        {
                            IsBackground = true
                        };
                        clientThread.Start();
                    }
                    else
                    {
                        Thread.Sleep(100);
                    }
                }
            }
            catch (SocketException) when (!_running) { }
            catch (Exception ex)
            {
                Log($"[Servidor] ERRO: {ex.Message}");
            }
            finally
            {
                _listener?.Stop();
            }
        }

        private void TratarGateway(TcpClient client, string endpoint)
        {
            string gatewayId = "?";
            bool gwConnected = false;

            try
            {
                using NetworkStream stream = client.GetStream();
                using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                using StreamWriter writer = new StreamWriter(stream, Encoding.UTF8)
                {
                    AutoFlush = true,
                    NewLine = "\n"
                };

                string? linha;
                while (_running && (linha = reader.ReadLine()) != null)
                {
                    linha = linha.Trim();
                    if (string.IsNullOrEmpty(linha))
                        continue;

                    Log($"[← {gatewayId}] {linha}");
                    Interlocked.Increment(ref _totalMessages);

                    string resposta = ProcessarMensagem(linha, ref gatewayId, ref gwConnected, endpoint);
                    writer.WriteLine(resposta);
                    Log($"[→ {gatewayId}] {resposta}");

                    if (linha.StartsWith("GW_DISCONNECT"))
                        break;
                }
            }
            catch (IOException)
            {
                Log($"[Servidor] Gateway {gatewayId} desligou-se abruptamente.");
            }
            catch (Exception ex)
            {
                Log($"[Servidor] Erro com gateway {gatewayId}: {ex.Message}");
            }
            finally
            {
                lock (_gwListLock)
                {
                    _gatewaysLigados.Remove(gatewayId);
                    _gwInfo.Remove(gatewayId);
                }
                client.Close();
                Log($"[Servidor] Ligação encerrada: {gatewayId} ({endpoint})");
            }
        }

        private string ProcessarMensagem(string mensagem, ref string gatewayId, ref bool gwConnected, string endpoint)
        {
            string[] partes = mensagem.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 0) return "ERR_INVALID_DATA";

            string comando = partes[0];

            if (!gwConnected && comando != "GW_CONNECT")
            {
                Log($"[Servidor] Comando '{comando}' recebido antes de GW_CONNECT — rejeitado.");
                return "ERR_SEQUENCE";
            }

            switch (comando)
            {
                case "GW_CONNECT":
                    if (partes.Length != 2) return "ERR_INVALID_DATA";
                    string gwId = partes[1];
                    if (string.IsNullOrWhiteSpace(gwId)) return "ERR_INVALID_DATA";

                    lock (_gwListLock)
                    {
                        _gatewaysLigados.Add(gwId);
                        _gwInfo[gwId] = (endpoint, DateTime.UtcNow);
                        _gwMessageCounts[gwId] = 0;
                    }

                    gatewayId = gwId;
                    gwConnected = true;
                    Log($"[Servidor] Gateway conectado: {gwId}");
                    return $"OK_GW_CONNECTED {gwId}";

                case "FORWARD":
                    if (partes.Length != 6) return "ERR_INVALID_DATA";

                    lock (_gwListLock)
                    {
                        if (_gwMessageCounts.ContainsKey(gatewayId))
                            _gwMessageCounts[gatewayId]++;
                    }

                    string sensorId = partes[1];
                    string tipoDado = partes[2];
                    string valor = partes[3];
                    string zona = partes[4];
                    string timestamp = partes[5];

                    var resultado = _dataStore.ArmazenarMedicao(sensorId, tipoDado, valor, zona, timestamp);

                    if (resultado == ResultadoArmazenamento.Sucesso)
                    {
                        OnDataStored?.Invoke(sensorId, tipoDado, valor, zona, timestamp);
                        return "OK";
                    }
                    return resultado == ResultadoArmazenamento.ErroStorage ? "ERR_STORAGE_FULL" : "ERR_INVALID_DATA";

                case "SENSOR_STATUS":
                    if (partes.Length != 3) return "ERR_INVALID_DATA";
                    string sid = partes[1];
                    string estado = partes[2];
                    if (!EstadosValidos.Contains(estado)) return "ERR_INVALID_DATA";
                    _dataStore.RegistarEstadoSensor(sid, estado);
                    Log($"[Servidor] Sensor {sid} (via {gatewayId}): estado -> {estado}");
                    return "OK_STATUS_RECEIVED";

                case "GW_DISCONNECT":
                    if (partes.Length != 2) return "ERR_INVALID_DATA";
                    if (partes[1] != gatewayId) return "ERR_INVALID_DATA";
                    lock (_gwListLock) { _gatewaysLigados.Remove(partes[1]); }
                    Log($"[Servidor] Gateway desconectado: {partes[1]}");
                    return "OK_GW_DISCONNECT";

                default:
                    return "ERR_INVALID_DATA";
            }
        }

        public List<string> GetGateways()
        {
            lock (_gwListLock)
            {
                return new List<string>(_gatewaysLigados);
            }
        }

        public List<(string Id, string Endpoint, DateTime Since, int Messages)> GetGatewayDetails()
        {
            var result = new List<(string, string, DateTime, int)>();
            lock (_gwListLock)
            {
                foreach (var gw in _gatewaysLigados)
                {
                    string ep = _gwInfo.ContainsKey(gw) ? _gwInfo[gw].Endpoint : "?";
                    DateTime since = _gwInfo.ContainsKey(gw) ? _gwInfo[gw].Since : DateTime.MinValue;
                    int msgs = _gwMessageCounts.ContainsKey(gw) ? _gwMessageCounts[gw] : 0;
                    result.Add((gw, ep, since, msgs));
                }
            }
            return result;
        }

        public List<MedicaoEntry> GetRecentData(string? tipo = null, string? zona = null, string? sensorId = null, int limit = 50)
        {
            return _dataStore.GetRecentData(tipo, zona, sensorId, limit);
        }
    }
}
