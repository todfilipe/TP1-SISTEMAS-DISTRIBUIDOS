using System;
using System.Collections.Generic;
using System.Threading;

namespace Gateway
{
    /// <summary>
    /// Monitor de heartbeats dos sensores.
    /// Corre numa thread de background e verifica periodicamente se algum sensor
    /// excedeu o timeout (LastSync demasiado antigo). Se sim, marca-o como "indisponivel"
    /// e notifica o Servidor via SENSOR_STATUS.
    /// </summary>
    public class HeartbeatGateway
    {
        private readonly SensorConfigManager _configManager;
        private readonly Func<string, string> _sendToServer;
        private readonly int _timeoutSeconds;
        private readonly int _checkIntervalMs;

        private Thread _monitorThread;
        private volatile bool _running;

        // Estados que não devem ser processados pelo monitor
        private static readonly HashSet<string> EstadosIgnorados = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "indisponivel", "desligado", "desativado", "manutencao"
        };

        /// <summary>
        /// Cria uma nova instância do monitor de heartbeats.
        /// </summary>
        /// <param name="configManager">Gestor de configuração dos sensores.</param>
        /// <param name="sendToServer">Delegate para enviar mensagens ao Servidor (com lock).</param>
        /// <param name="timeoutSeconds">Segundos sem heartbeat para considerar timeout (default: 15).</param>
        /// <param name="checkIntervalMs">Intervalo entre verificações em milissegundos (default: 5000).</param>
        public HeartbeatGateway(SensorConfigManager configManager, Func<string, string> sendToServer,
                                int timeoutSeconds = 15, int checkIntervalMs = 5000)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            _sendToServer = sendToServer ?? throw new ArgumentNullException(nameof(sendToServer));
            _timeoutSeconds = timeoutSeconds;
            _checkIntervalMs = checkIntervalMs;
        }

        /// <summary>
        /// Inicia a thread de monitorização em background.
        /// </summary>
        public void Start()
        {
            _running = true;
            _monitorThread = new Thread(MonitorLoop)
            {
                IsBackground = true,
                Name = "HeartbeatMonitor"
            };
            _monitorThread.Start();
            Console.WriteLine($"[HEARTBEAT] Monitor iniciado (timeout: {_timeoutSeconds}s, intervalo: {_checkIntervalMs}ms).");
        }

        /// <summary>
        /// Sinaliza paragem limpa da thread de monitorização.
        /// </summary>
        public void Stop()
        {
            _running = false;
            Console.WriteLine("[HEARTBEAT] Monitor parado.");
        }

        /// <summary>
        /// Ciclo principal do monitor: verifica periodicamente o LastSync de cada sensor.
        /// </summary>
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
                    Console.WriteLine($"[HEARTBEAT] Erro na verificação: {ex.Message}");
                }

                Thread.Sleep(_checkIntervalMs);
            }
        }

        /// <summary>
        /// Verifica todos os sensores e marca como "indisponivel" os que excederam o timeout.
        /// </summary>
        private void CheckSensors()
        {
            List<SensorConfig> sensores = _configManager.GetAllSensors();

            foreach (SensorConfig sensor in sensores)
            {
                if (sensor == null)
                    continue;

                // Ignorar sensores em estados que não devem ser processados
                if (EstadosIgnorados.Contains(sensor.Estado))
                    continue;

                // Verificar se excedeu o timeout
                TimeSpan elapsed = DateTime.UtcNow - sensor.LastSync;
                if (elapsed.TotalSeconds > _timeoutSeconds)
                {
                    // 1. Marcar como indisponivel
                    _configManager.ChangeSensorStatus(sensor.SensorId, "indisponivel");

                    // 2. Notificar o Servidor
                    string response = _sendToServer($"SENSOR_STATUS {sensor.SensorId} indisponivel");

                    // 3. Registar no log
                    Console.WriteLine($"[HEARTBEAT] Sensor '{sensor.SensorId}' timeout — marcado como indisponivel.");

                    if (response != null && response.StartsWith("OK_STATUS_RECEIVED"))
                    {
                        Console.WriteLine($"[HEARTBEAT] Servidor confirmou estado de '{sensor.SensorId}': {response}");
                    }
                    else
                    {
                        Console.WriteLine($"[HEARTBEAT] AVISO: Resposta inesperada do Servidor para '{sensor.SensorId}': {response}");
                    }
                }
            }
        }
    }
}
