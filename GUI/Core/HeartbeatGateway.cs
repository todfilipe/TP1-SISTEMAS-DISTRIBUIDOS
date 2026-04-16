using System;
using System.Collections.Generic;
using System.Threading;

namespace OneHealthMonitor.Core
{
    /// <summary>
    /// Monitor de heartbeats dos sensores.
    /// Verifica periodicamente se algum sensor excedeu o timeout.
    /// </summary>
    public class HeartbeatGateway
    {
        private readonly SensorConfigManager _configManager;
        private readonly Func<string, string?> _sendToServer;
        private int _timeoutSeconds;
        private int _checkIntervalMs;

        private Thread? _monitorThread;
        private volatile bool _running;

        private static readonly HashSet<string> EstadosIgnorados = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "indisponivel", "desligado", "desativado", "manutencao"
        };

        public event Action<string>? OnLogMessage;

        public int TimeoutSeconds
        {
            get => _timeoutSeconds;
            set => _timeoutSeconds = value;
        }

        public int CheckIntervalMs
        {
            get => _checkIntervalMs;
            set => _checkIntervalMs = value;
        }

        public bool IsRunning => _running;

        public HeartbeatGateway(SensorConfigManager configManager, Func<string, string?> sendToServer,
                                int timeoutSeconds = 15, int checkIntervalMs = 5000)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            _sendToServer = sendToServer ?? throw new ArgumentNullException(nameof(sendToServer));
            _timeoutSeconds = timeoutSeconds;
            _checkIntervalMs = checkIntervalMs;
        }

        private void Log(string msg) => OnLogMessage?.Invoke(msg);

        public void Start()
        {
            _running = true;
            _monitorThread = new Thread(MonitorLoop)
            {
                IsBackground = true,
                Name = "HeartbeatMonitor"
            };
            _monitorThread.Start();
            Log($"[HEARTBEAT] Monitor iniciado (timeout: {_timeoutSeconds}s, intervalo: {_checkIntervalMs}ms).");
        }

        public void Stop()
        {
            _running = false;
            Log("[HEARTBEAT] Monitor parado.");
        }

        private void MonitorLoop()
        {
            while (_running)
            {
                try
                {
                    CheckSensors();
                }
                catch (Exception ex)
                {
                    Log($"[HEARTBEAT] Erro na verificação: {ex.Message}");
                }

                Thread.Sleep(_checkIntervalMs);
            }
        }

        private void CheckSensors()
        {
            List<SensorConfig> sensores = _configManager.GetAllSensors();

            foreach (SensorConfig sensor in sensores)
            {
                if (sensor == null)
                    continue;

                if (EstadosIgnorados.Contains(sensor.Estado))
                    continue;

                TimeSpan elapsed = DateTime.UtcNow - sensor.LastSync;
                if (elapsed.TotalSeconds > _timeoutSeconds)
                {
                    _configManager.ChangeSensorStatus(sensor.SensorId, "indisponivel");

                    string? response = _sendToServer($"SENSOR_STATUS {sensor.SensorId} indisponivel");
                    Log($"[HEARTBEAT] Sensor '{sensor.SensorId}' timeout — marcado como indisponivel.");

                    if (response != null && response.StartsWith("OK_STATUS_RECEIVED"))
                    {
                        Log($"[HEARTBEAT] Servidor confirmou estado de '{sensor.SensorId}'.");
                    }
                }
            }
        }
    }
}
