using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Linq;

namespace Gateway
{
    internal sealed class VideoStreamHandler : IDisposable
    {
        private readonly int _port;
        private readonly SensorConfigManager _configManager;
        private readonly Func<string, string> _sendToServer;
        private readonly RetryBuffer _retryBuffer;
        private readonly Mutex _videoLogMutex = new Mutex(false, "GatewayVideoLogMutex");
        private TcpListener _listener = null!;
        private Thread _thread = null!;
        private volatile bool _running;

        public VideoStreamHandler(int port, SensorConfigManager configManager, Func<string, string> sendToServer, RetryBuffer retryBuffer)
        {
            _port = port;
            _configManager = configManager;
            _sendToServer = sendToServer;
            _retryBuffer = retryBuffer;
        }

        public void Start()
        {
            _running = true;
            _thread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "VideoStreamListener"
            };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _listener?.Stop();
        }

        private void ListenLoop()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                Console.WriteLine($"[GATEWAY] A escutar streams de vídeo na porta {_port}...");

                while (_running)
                {
                    if (_listener.Pending())
                    {
                        TcpClient videoClient = _listener.AcceptTcpClient();
                        var handler = new Thread(() => Handle(videoClient)) { IsBackground = true };
                        handler.Start();
                    }
                    else
                    {
                        Thread.Sleep(100);
                    }
                }
            }
            catch (SocketException) when (!_running)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Falha no listener de vídeo: {ex.Message}");
            }
            finally
            {
                _listener?.Stop();
            }
        }

        private void Handle(TcpClient client)
        {
            string sensorId = "UNKNOWN";
            DateTime startTime = DateTime.UtcNow;
            int frameCount = 0;

            try
            {
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" })
                {
                    string? firstLine = reader.ReadLine();
                    if (firstLine == null)
                    {
                        return;
                    }

                    string[] parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || parts[0] != "VIDEO_STREAM")
                    {
                        return;
                    }

                    sensorId = parts[1];
                    SensorConfig config = _configManager.GetSensor(sensorId);
                    if (config == null || string.IsNullOrWhiteSpace(config.Estado) || !config.Estado.Equals("ativo", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[VIDEO] Conexão rejeitada para '{sensorId}': Sensor não registado ou não ativo.");
                        writer.WriteLine("ERR_NOT_REGISTERED");
                        return;
                    }

                    if (config.TiposDados == null || !config.TiposDados.Contains("VIDEO", StringComparer.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[VIDEO] Conexão rejeitada para '{sensorId}': Não tem permissão para transmitir 'VIDEO'.");
                        writer.WriteLine("ERR_INVALID_TYPE");
                        return;
                    }

                    Console.WriteLine($"[VIDEO] Stream iniciada pelo sensor '{sensorId}'.");
                    writer.WriteLine("OK_VIDEO_STARTED");
                    startTime = DateTime.UtcNow;

                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] lineParts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (lineParts.Length == 0)
                        {
                            continue;
                        }

                        if (lineParts[0] == "FRAME")
                        {
                            frameCount++;
                            Console.WriteLine($"[VIDEO] Sensor '{sensorId}' - frame {(lineParts.Length >= 2 ? lineParts[1] : frameCount.ToString())} recebido.");
                        }
                        else if (lineParts[0] == "STREAM_END")
                        {
                            Console.WriteLine($"[VIDEO] Stream do sensor '{sensorId}' terminada ({frameCount} frames recebidos).");
                            break;
                        }
                    }
                }

                SendVideoMetadata(sensorId, startTime, frameCount);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO VIDEO '{sensorId}'] {ex.Message}");
            }
            finally
            {
                SaveMetadataLog(sensorId, startTime);
                client.Close();
            }
        }

        private void SendVideoMetadata(string sensorId, DateTime startTime, int frameCount)
        {
            string zone = _configManager.GetSensor(sensorId)?.Zona ?? "ZONA_CENTRO";
            string timestamp = startTime.ToString("yyyy-MM-ddTHH:mm:ss");
            string forward = $"FORWARD {sensorId} VIDEO {frameCount} {zone} {timestamp}";

            string response = _sendToServer(forward);
            if (response == null)
            {
                Console.WriteLine($"[VIDEO] Servidor inalcançável - mensagem colocada no buffer de retentativa: {forward}");
                _retryBuffer.Enqueue(forward);
            }
            else if (response.StartsWith("OK"))
            {
                Console.WriteLine($"[VIDEO] Servidor aceitou dados de vídeo do sensor '{sensorId}'. Resposta: {response}");
            }
            else
            {
                Console.WriteLine($"[VIDEO] Servidor rejeitou dados de vídeo do sensor '{sensorId}'. Resposta: {response}");
            }
        }

        private void SaveMetadataLog(string sensorId, DateTime startTime)
        {
            TimeSpan duration = DateTime.UtcNow - startTime;
            _videoLogMutex.WaitOne();
            try
            {
                string logFile = "video_metadata.log";
                bool writeHeader = !File.Exists(logFile) || new FileInfo(logFile).Length == 0;

                using (var logWriter = new StreamWriter(logFile, append: true, Encoding.UTF8))
                {
                    if (writeHeader)
                    {
                        logWriter.WriteLine("sensor_id,inicio_utc,duracao_segundos");
                    }

                    logWriter.WriteLine($"{sensorId},{startTime:yyyy-MM-ddTHH:mm:ss},{duration.TotalSeconds:F2}");
                }
            }
            finally
            {
                _videoLogMutex.ReleaseMutex();
            }

            Console.WriteLine($"[VIDEO] Metadados do sensor '{sensorId}' registados em video_metadata.log.");
        }

        public void Dispose()
        {
            Stop();
            _videoLogMutex.Dispose();
        }
    }
}
