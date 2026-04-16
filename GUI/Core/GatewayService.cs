using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace OneHealthMonitor.Core
{
    /// <summary>
    /// Gateway in-process que aceita sensores TCP e encaminha dados ao Servidor.
    /// Encapsula a lógica de Gateway/Program.cs.
    /// </summary>
    public class GatewayService
    {
        private string _gatewayId = "GW01";
        private TcpClient? _serverClient;
        private StreamReader? _serverReader;
        private StreamWriter? _serverWriter;
        private volatile bool _running;

        private SensorConfigManager? _configManager;
        private HeartbeatGateway? _heartbeatMonitor;
        private RetryBuffer? _retryBuffer;

        private TcpListener? _sensorListener;
        private TcpListener? _videoListener;
        private Thread? _sensorThread;
        private Thread? _videoThread;

        private readonly object _serverLock = new object();
        private readonly object _videoLogLock = new object();
        private readonly HashSet<string> _activeSessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _sessionLock = new object();

        private int _totalForwards;

        public event Action<string>? OnLogMessage;
        public event Action<SensorConfig>? OnSensorUpdated;
        public event Action<LiveDataItem>? OnDataReceived;

        public bool IsRunning => _running;
        public bool IsConnectedToServer => _serverClient?.Connected ?? false;
        public string GatewayId => _gatewayId;
        public int BufferCount => _retryBuffer?.Count ?? 0;
        public int BufferMax => _retryBuffer?.MaxCount ?? 1000;
        public int TotalForwards => _totalForwards;
        public SensorConfigManager? ConfigManager => _configManager;
        public HeartbeatGateway? HeartbeatMonitor => _heartbeatMonitor;
        public RetryBuffer? Buffer => _retryBuffer;

        private void Log(string msg) => OnLogMessage?.Invoke(msg);

        public void Start(string gatewayId, string serverIp, int serverPort, int sensorPort, int videoPort, string csvPath)
        {
            if (_running) return;

            _gatewayId = gatewayId;
            _running = true;

            // 1. Ligar ao Servidor
            try
            {
                _serverClient = new TcpClient(serverIp, serverPort);
                var stream = _serverClient.GetStream();
                _serverReader = new StreamReader(stream, Encoding.UTF8);
                _serverWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };

                _serverWriter.WriteLine($"GW_CONNECT {_gatewayId}");
                Log($"[GATEWAY] GW_CONNECT {_gatewayId} enviado ao Servidor.");

                string? response = _serverReader.ReadLine();
                if (response == null || !response.StartsWith("OK_GW_CONNECTED"))
                {
                    Log($"[ERRO] Falha ao ligar ao Servidor. Resposta: {response}");
                    _running = false;
                    return;
                }

                Log("[GATEWAY] Ligado ao Servidor com sucesso.");
            }
            catch (Exception ex)
            {
                Log($"[ERRO] Não foi possível ligar ao Servidor ({serverIp}:{serverPort}): {ex.Message}");
                _running = false;
                return;
            }

            // 2. Carregar configuração CSV
            _configManager = new SensorConfigManager(csvPath);
            _configManager.OnLogMessage += msg => Log(msg);
            int loaded = _configManager.LoadConfig();
            Log($"[GATEWAY] Configuração carregada ({loaded} sensor(es)).");

            // 3. Iniciar RetryBuffer
            _retryBuffer = new RetryBuffer(SendToServer);
            _retryBuffer.OnLogMessage += msg => Log(msg);
            _retryBuffer.Start();

            // 4. Iniciar HeartbeatMonitor
            _heartbeatMonitor = new HeartbeatGateway(_configManager, SendToServer);
            _heartbeatMonitor.OnLogMessage += msg => Log(msg);
            _heartbeatMonitor.Start();

            // 5. Listener de vídeo
            _videoThread = new Thread(() => RunVideoListener(videoPort))
            {
                IsBackground = true,
                Name = "GW-VideoListener"
            };
            _videoThread.Start();

            // 6. Listener de sensores
            _sensorThread = new Thread(() => RunSensorListener(sensorPort))
            {
                IsBackground = true,
                Name = "GW-SensorListener"
            };
            _sensorThread.Start();
        }

        public void Stop()
        {
            _running = false;
            _retryBuffer?.Stop();
            _heartbeatMonitor?.Stop();

            try { _sensorListener?.Stop(); } catch { }
            try { _videoListener?.Stop(); } catch { }

            lock (_serverLock)
            {
                try
                {
                    _serverWriter?.WriteLine($"GW_DISCONNECT {_gatewayId}");
                    Log($"[GATEWAY] GW_DISCONNECT enviado ao Servidor.");
                }
                catch { }
            }

            try { _serverClient?.Close(); } catch { }
            Log("[GATEWAY] Parado.");
        }

        private string? SendToServer(string message)
        {
            lock (_serverLock)
            {
                try
                {
                    _serverWriter?.WriteLine(message);
                    string? response = _serverReader?.ReadLine();
                    return response;
                }
                catch (Exception ex)
                {
                    Log($"[ERRO] Falha na comunicação com o Servidor: {ex.Message}");
                    return null;
                }
            }
        }

        public List<SensorConfig> GetConnectedSensors()
        {
            return _configManager?.GetAllSensors() ?? new List<SensorConfig>();
        }

        private void RunSensorListener(int port)
        {
            try
            {
                _sensorListener = new TcpListener(IPAddress.Any, port);
                _sensorListener.Start();
                Log($"[GATEWAY] A escutar Sensores na porta {port}...");

                while (_running)
                {
                    if (_sensorListener.Pending())
                    {
                        TcpClient sensorClient = _sensorListener.AcceptTcpClient();
                        Thread handler = new Thread(() => HandleSensor(sensorClient))
                        {
                            IsBackground = true
                        };
                        handler.Start();
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
                Log($"[ERRO] Listener de Sensores: {ex.Message}");
            }
            finally
            {
                _sensorListener?.Stop();
            }
        }

        private void RunVideoListener(int port)
        {
            try
            {
                _videoListener = new TcpListener(IPAddress.Any, port);
                _videoListener.Start();
                Log($"[GATEWAY] A escutar vídeo na porta {port}...");

                while (_running)
                {
                    if (_videoListener.Pending())
                    {
                        TcpClient videoClient = _videoListener.AcceptTcpClient();
                        Thread handler = new Thread(() => HandleVideoStream(videoClient))
                        {
                            IsBackground = true
                        };
                        handler.Start();
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
                Log($"[ERRO] Listener de Vídeo: {ex.Message}");
            }
            finally
            {
                _videoListener?.Stop();
            }
        }

        private void HandleSensor(TcpClient sensorClient)
        {
            string currentSensorId = "UNKNOWN";
            int state = 0; // 0=AGUARDA_CONNECT, 1=AGUARDA_REGISTER, 2=OPERACIONAL
            bool handshakeCompleted = false;
            List<string> sessionTypes = new List<string>();

            Timer handshakeTimer = new Timer(_ =>
            {
                if (!handshakeCompleted)
                {
                    Log($"[TIMEOUT] Sensor '{currentSensorId}' não completou handshake — a fechar.");
                    try { sensorClient.Close(); } catch { }
                }
            }, null, 10000, Timeout.Infinite);

            try
            {
                using var stream = sensorClient.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };

                string? line;
                while (_running && (line = reader.ReadLine()) != null)
                {
                    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) continue;

                    string command = parts[0];
                    Log($"[← Sensor '{currentSensorId}'] {line}");

                    switch (command)
                    {
                        case "CONNECT":
                            if (parts.Length < 2) { writer.WriteLine("ERR_INVALID_DATA"); break; }
                            if (state != 0) { writer.WriteLine("ERR_SEQUENCE"); break; }

                            currentSensorId = parts[1];
                            SensorConfig? sensorCfg = _configManager?.GetSensor(currentSensorId);

                            if (sensorCfg == null)
                            {
                                Log($"[CONFIG] Sensor '{currentSensorId}' não existe no CSV.");
                                writer.WriteLine("ERR_NOT_REGISTERED");
                                return;
                            }

                            string estadoActual = sensorCfg.Estado?.ToLower() ?? "";
                            if (estadoActual == "desativado" || estadoActual == "manutencao")
                            {
                                Log($"[SENSOR '{currentSensorId}'] rejeitado — estado: {sensorCfg.Estado}.");
                                writer.WriteLine("ERR_SENSOR_INACTIVE");
                                return;
                            }

                            lock (_sessionLock)
                            {
                                if (_activeSessions.Contains(currentSensorId))
                                {
                                    writer.WriteLine("ERR_ALREADY_CONNECTED");
                                    return;
                                }
                                _activeSessions.Add(currentSensorId);
                            }

                            state = 1;
                            Log($"[SENSOR '{currentSensorId}'] conectou-se.");
                            writer.WriteLine($"OK_CONNECTED {currentSensorId}");
                            OnSensorUpdated?.Invoke(sensorCfg);
                            break;

                        case "REGISTER_TYPES":
                            if (parts.Length < 2) { writer.WriteLine("ERR_INVALID_DATA"); break; }
                            if (state != 1) { writer.WriteLine("ERR_SEQUENCE"); break; }

                            string[] tiposEnviados = parts[1].Split(',');
                            SensorConfig? configSensor = _configManager?.GetSensor(currentSensorId);

                            bool tiposValidos = true;
                            if (configSensor != null)
                            {
                                foreach (string tipo in tiposEnviados)
                                {
                                    if (!configSensor.TiposDados.Contains(tipo, StringComparer.OrdinalIgnoreCase))
                                    {
                                        tiposValidos = false;
                                        break;
                                    }
                                }
                            }

                            if (!tiposValidos)
                            {
                                writer.WriteLine("ERR_TYPE_NOT_SUPPORTED");
                                break;
                            }

                            sessionTypes = new List<string>(tiposEnviados);
                            state = 2;
                            handshakeCompleted = true;
                            handshakeTimer.Dispose();
                            Log($"[SENSOR '{currentSensorId}'] registou tipos: {parts[1]}");
                            writer.WriteLine("OK_TYPES_REGISTERED");
                            break;

                        case "DATA":
                            if (state != 2) { writer.WriteLine("ERR_SEQUENCE"); break; }

                            Log($"[SENSOR '{currentSensorId}'] DATA: {line}");

                            DataValidationResult validationResult =
                                DataValidator.ValidateAndProcessData(line, currentSensorId, _configManager!, sessionTypes);

                            Log(validationResult.LogMessage ?? "");

                            if (!validationResult.IsValid)
                            {
                                writer.WriteLine(validationResult.ErrorCode);
                                break;
                            }

                            var sensorAtual = _configManager?.GetSensor(currentSensorId);
                            if (sensorAtual != null && (sensorAtual.Estado == "indisponivel" || sensorAtual.Estado == "desligado"))
                            {
                                _configManager?.ChangeSensorStatus(currentSensorId, "ativo");
                            }

                            string? serverResponse = SendToServer(validationResult.ForwardMessage!);

                            if (serverResponse != null && serverResponse.StartsWith("OK"))
                            {
                                writer.WriteLine("OK");
                                Interlocked.Increment(ref _totalForwards);

                                // Parse and emit live data event
                                string[] dp = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (dp.Length >= 5)
                                {
                                    var item = LiveDataItem.Create(currentSensorId, dp[1], dp[2], dp[3], dp[4]);
                                    OnDataReceived?.Invoke(item);
                                }
                            }
                            else
                            {
                                Log($"[AVISO] Servidor indisponível — mensagem adicionada ao buffer.");
                                _retryBuffer?.Enqueue(validationResult.ForwardMessage!);
                                writer.WriteLine("OK");
                            }
                            break;

                        case "HEARTBEAT":
                            if (state != 2) { writer.WriteLine("ERR_SEQUENCE"); break; }
                            Log($"[SENSOR '{currentSensorId}'] heartbeat.");
                            var sensorHb = _configManager?.GetSensor(currentSensorId);
                            if (sensorHb != null && (sensorHb.Estado == "indisponivel" || sensorHb.Estado == "desligado"))
                            {
                                _configManager?.ChangeSensorStatus(currentSensorId, "ativo");
                            }
                            writer.WriteLine("OK");
                            _configManager?.UpdateLastSync(currentSensorId, DateTime.UtcNow);
                            break;

                        case "DISCONNECT":
                            if (parts.Length < 2) { writer.WriteLine("ERR_INVALID_DATA"); break; }
                            if (state == 0) { writer.WriteLine("ERR_SEQUENCE"); break; }
                            if (parts[1] != currentSensorId) { writer.WriteLine("ERR_INVALID_DATA"); break; }

                            Log($"[SENSOR '{currentSensorId}'] desconexão.");
                            writer.WriteLine("OK_DISCONNECT");
                            _configManager?.ChangeSensorStatus(currentSensorId, "desligado");
                            SendToServer($"SENSOR_STATUS {currentSensorId} desligado");
                            return;

                        default:
                            writer.WriteLine("ERR_INVALID_DATA");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ERRO SENSOR '{currentSensorId}'] {ex.Message}");
            }
            finally
            {
                handshakeTimer.Dispose();
                sensorClient.Close();
                lock (_sessionLock) { _activeSessions.Remove(currentSensorId); }
                Log($"[GATEWAY] Sensor '{currentSensorId}' finalizado.");
                OnSensorUpdated?.Invoke(_configManager?.GetSensor(currentSensorId) ?? new SensorConfig { SensorId = currentSensorId, Estado = "desligado" });
            }
        }

        private void HandleVideoStream(TcpClient client)
        {
            string sensorId = "UNKNOWN";
            DateTime startTime = DateTime.UtcNow;
            int frameCount = 0;

            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };

                string? firstLine = reader.ReadLine();
                if (firstLine == null) return;

                string[] parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[0] != "VIDEO_STREAM") return;

                sensorId = parts[1];
                Log($"[VIDEO] Stream iniciada por '{sensorId}'.");
                writer.WriteLine("OK_VIDEO_STARTED");
                startTime = DateTime.UtcNow;

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] lp = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (lp.Length == 0) continue;

                    if (lp[0] == "FRAME")
                    {
                        frameCount++;
                        Log($"[VIDEO] '{sensorId}' — frame {(lp.Length >= 2 ? lp[1] : frameCount.ToString())} recebido.");
                    }
                    else if (lp[0] == "STREAM_END")
                    {
                        Log($"[VIDEO] Stream de '{sensorId}' terminada ({frameCount} frames).");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ERRO VIDEO '{sensorId}'] {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }
    }
}
