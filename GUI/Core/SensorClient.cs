using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace OneHealthMonitor.Core
{
    /// <summary>
    /// Lógica de comunicação TCP do Sensor com o Gateway.
    /// Adaptado de Sensor/Sensor.cs para uso no GUI com evento de log.
    /// </summary>
    public class SensorClient : IDisposable
    {
        private TcpClient? _client;
        private StreamReader? _reader;
        private StreamWriter? _writer;

        private readonly string _sensorId;
        private readonly string _gatewayHost;
        private readonly int _gatewayPort;

        private bool _connected;
        private bool _typesRegistered;

        private Thread? _heartbeatThread;
        private volatile bool _heartbeatRunning;

        private readonly object _sendLock = new object();

        private const int TimeoutMs = 10000;
        private const int HeartbeatIntervalMs = 5000;

        public event Action<string, bool>? OnLogMessage; // message, isSent (true=→, false=←)

        public string SensorId => _sensorId;
        public bool IsConnected => _connected;
        public bool IsTypesRegistered => _typesRegistered;
        public bool IsOperational => _connected && _typesRegistered;
        public bool IsHeartbeatRunning => _heartbeatRunning;

        public SensorClient(string sensorId, string gatewayHost, int gatewayPort = 8080)
        {
            _sensorId = sensorId;
            _gatewayHost = gatewayHost;
            _gatewayPort = gatewayPort;
        }

        private void LogSent(string msg) => OnLogMessage?.Invoke(msg, true);
        private void LogReceived(string msg) => OnLogMessage?.Invoke(msg, false);

        public void ConnectTcp()
        {
            _client = new TcpClient();

            var connectTask = _client.ConnectAsync(_gatewayHost, _gatewayPort);
            if (!connectTask.Wait(TimeoutMs))
            {
                _client.Close();
                throw new TimeoutException($"Timeout ao ligar ao Gateway {_gatewayHost}:{_gatewayPort}.");
            }

            _client.ReceiveTimeout = TimeoutMs;

            var stream = _client.GetStream();
            _reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            _writer = new StreamWriter(stream, System.Text.Encoding.UTF8)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
        }

        public string SendConnect()
        {
            lock (_sendLock)
            {
                string msg = $"CONNECT {_sensorId}";
                SendMessage(msg);
                LogSent(msg);
                string response = ReadResponse();
                LogReceived(response);

                if (response == $"OK_CONNECTED {_sensorId}")
                    _connected = true;

                return response;
            }
        }

        public string SendRegisterTypes(List<string> types)
        {
            if (!_connected)
                return "ERR: Deve enviar CONNECT primeiro.";

            lock (_sendLock)
            {
                string typesStr = string.Join(",", types);
                string msg = $"REGISTER_TYPES {typesStr}";
                SendMessage(msg);
                LogSent(msg);
                string response = ReadResponse();
                LogReceived(response);

                if (response == "OK_TYPES_REGISTERED")
                    _typesRegistered = true;

                return response;
            }
        }

        public string SendData(string tipo, string valor, string zona, string? timestamp = null)
        {
            if (!IsOperational)
                return "ERR: Deve completar CONNECT e REGISTER_TYPES primeiro.";

            timestamp ??= DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            lock (_sendLock)
            {
                string msg = $"DATA {tipo} {valor} {zona} {timestamp}";
                SendMessage(msg);
                LogSent(msg);
                string response = ReadResponse();
                LogReceived(response);
                return response;
            }
        }

        public string SendHeartbeat()
        {
            if (!_connected)
                return "ERR: Não está conectado.";

            lock (_sendLock)
            {
                string msg = $"HEARTBEAT {_sensorId}";
                SendMessage(msg);
                LogSent(msg);
                string response = ReadResponse();
                LogReceived(response);
                return response;
            }
        }

        public void StartHeartbeatAuto()
        {
            if (_heartbeatRunning) return;

            _heartbeatRunning = true;
            _heartbeatThread = new Thread(() =>
            {
                while (_heartbeatRunning && _connected)
                {
                    try
                    {
                        Thread.Sleep(HeartbeatIntervalMs);
                        if (!_heartbeatRunning || !_connected) break;

                        lock (_sendLock)
                        {
                            string msg = $"HEARTBEAT {_sensorId}";
                            SendMessage(msg);
                            LogSent(msg);
                            string resp = ReadResponse();
                            LogReceived(resp);
                        }
                    }
                    catch (Exception)
                    {
                        _heartbeatRunning = false;
                    }
                }
            })
            {
                IsBackground = true,
                Name = $"Heartbeat-{_sensorId}"
            };
            _heartbeatThread.Start();
        }

        public void StopHeartbeatAuto()
        {
            _heartbeatRunning = false;
        }

        public string SendVideoStream(int videoPort, int frameCount, int frameIntervalMs = 100)
        {
            if (!IsOperational)
                return "ERR: Deve completar CONNECT e REGISTER_TYPES primeiro.";

            TcpClient? videoClient = null;
            try
            {
                videoClient = new TcpClient();
                var connectTask = videoClient.ConnectAsync(_gatewayHost, videoPort);
                if (!connectTask.Wait(TimeoutMs))
                {
                    videoClient.Close();
                    return "ERR: Timeout ao ligar à porta de vídeo.";
                }

                var stream = videoClient.GetStream();
                var vWriter = new StreamWriter(stream, System.Text.Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };
                var vReader = new StreamReader(stream, System.Text.Encoding.UTF8);

                vWriter.WriteLine($"VIDEO_STREAM {_sensorId}");
                LogSent($"VIDEO_STREAM {_sensorId}");
                string resp = vReader.ReadLine() ?? "ERR: Sem resposta.";
                LogReceived(resp);

                if (resp != "OK_VIDEO_STARTED")
                {
                    videoClient.Close();
                    return $"ERR: Handshake vídeo falhou: {resp}";
                }

                for (int i = 1; i <= frameCount; i++)
                {
                    vWriter.WriteLine($"FRAME {i}");
                    LogSent($"FRAME {i}");
                    if (i < frameCount)
                        Thread.Sleep(frameIntervalMs);
                }

                vWriter.WriteLine("STREAM_END");
                LogSent("STREAM_END");

                vWriter.Dispose();
                vReader.Dispose();
                videoClient.Close();

                return "OK_VIDEO_STREAM_COMPLETE";
            }
            catch (Exception ex)
            {
                videoClient?.Close();
                return $"ERR: {ex.Message}";
            }
        }

        public string SendDisconnect()
        {
            if (!_connected)
                return "ERR: Não está conectado.";

            StopHeartbeatAuto();

            lock (_sendLock)
            {
                string msg = $"DISCONNECT {_sensorId}";
                SendMessage(msg);
                LogSent(msg);
                string response = ReadResponse();
                LogReceived(response);

                if (response == "OK_DISCONNECT")
                {
                    _connected = false;
                    _typesRegistered = false;
                }

                return response;
            }
        }

        private void SendMessage(string message)
        {
            if (_writer == null)
                throw new InvalidOperationException("Ligação TCP não estabelecida.");
            _writer.WriteLine(message);
        }

        private string ReadResponse()
        {
            if (_reader == null)
                throw new InvalidOperationException("Ligação TCP não estabelecida.");
            string? line = _reader.ReadLine();
            return line ?? "ERR: Ligação fechada pelo Gateway.";
        }

        public void Dispose()
        {
            StopHeartbeatAuto();

            if (_connected && _writer != null)
            {
                try
                {
                    SendMessage($"DISCONNECT {_sensorId}");
                    _reader?.ReadLine();
                }
                catch { }
            }

            _connected = false;
            _typesRegistered = false;
            _writer?.Dispose();
            _reader?.Dispose();
            _client?.Close();
            _client?.Dispose();
        }
    }
}
