using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OneHealthMonitor.Core
{
    /// <summary>
    /// Gestor do ficheiro de configuração sensors.csv.
    /// Mantém um dicionário thread-safe em memória e persiste alterações no ficheiro.
    /// </summary>
    public class SensorConfigManager
    {
        private readonly string _filePath;
        private readonly Dictionary<string, SensorConfig> _sensors;
        private readonly object _lock = new object();

        public event Action<string>? OnLogMessage;

        public SensorConfigManager(string filePath)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _sensors = new Dictionary<string, SensorConfig>(StringComparer.OrdinalIgnoreCase);
        }

        private void Log(string msg) => OnLogMessage?.Invoke(msg);

        public int LoadConfig()
        {
            lock (_lock)
            {
                _sensors.Clear();

                if (!File.Exists(_filePath))
                {
                    Log($"[CONFIG] AVISO: Ficheiro '{_filePath}' não encontrado. A iniciar com configuração vazia.");
                    return 0;
                }

                string[] lines;
                try
                {
                    lines = File.ReadAllLines(_filePath);
                }
                catch (Exception ex)
                {
                    Log($"[CONFIG] ERRO ao ler ficheiro '{_filePath}': {ex.Message}");
                    return 0;
                }

                int loaded = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                        continue;

                    SensorConfig? config = ParseLine(line, i + 1);
                    if (config != null)
                    {
                        _sensors[config.SensorId] = config;
                        loaded++;
                    }
                }

                Log($"[CONFIG] Carregados {loaded} sensor(es) a partir de '{_filePath}'.");
                return loaded;
            }
        }

        private SensorConfig? ParseLine(string line, int lineNumber)
        {
            int openBracket = line.IndexOf('[');
            int closeBracket = line.IndexOf(']');

            if (openBracket < 0 || closeBracket < 0 || closeBracket <= openBracket)
            {
                Log($"[CONFIG] AVISO: Linha {lineNumber} malformada (parênteses retos não encontrados): {line}");
                return null;
            }

            string beforeBrackets = line.Substring(0, openBracket);
            string insideBrackets = line.Substring(openBracket + 1, closeBracket - openBracket - 1);
            string afterBrackets = line.Substring(closeBracket + 1);

            string[] prefixParts = beforeBrackets.Split(':');
            if (prefixParts.Length < 4)
            {
                Log($"[CONFIG] AVISO: Linha {lineNumber} malformada (campos insuficientes antes dos tipos): {line}");
                return null;
            }

            string sensorId = prefixParts[0].Trim();
            string estado = prefixParts[1].Trim();
            string zona = prefixParts[2].Trim();

            if (string.IsNullOrEmpty(sensorId) || string.IsNullOrEmpty(estado) || string.IsNullOrEmpty(zona))
            {
                Log($"[CONFIG] AVISO: Linha {lineNumber} tem campos obrigatórios vazios: {line}");
                return null;
            }

            List<string> tiposDados = new List<string>();
            if (!string.IsNullOrWhiteSpace(insideBrackets))
            {
                string[] tipos = insideBrackets.Split(',');
                foreach (string t in tipos)
                {
                    string trimmed = t.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        tiposDados.Add(trimmed);
                }
            }

            string[] suffixParts = afterBrackets.Split(':');
            string timestampStr = "";
            if (suffixParts.Length > 1)
            {
                timestampStr = string.Join(":", suffixParts, 1, suffixParts.Length - 1).Trim();
            }

            DateTime lastSync;
            if (!DateTime.TryParse(timestampStr, CultureInfo.InvariantCulture,
                                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                    out lastSync))
            {
                Log($"[CONFIG] AVISO: Linha {lineNumber} — timestamp inválido '{timestampStr}': {line}");
                return null;
            }

            return new SensorConfig
            {
                SensorId = sensorId,
                Estado = estado,
                Zona = zona,
                TiposDados = tiposDados,
                LastSync = lastSync
            };
        }

        public bool UpdateLastSync(string sensorId, DateTime timestamp)
        {
            lock (_lock)
            {
                if (!_sensors.ContainsKey(sensorId))
                {
                    Log($"[CONFIG] AVISO: Sensor '{sensorId}' não encontrado na configuração.");
                    return false;
                }

                _sensors[sensorId].LastSync = timestamp;
                Log($"[CONFIG] Sensor '{sensorId}' — last_sync atualizado para {timestamp:yyyy-MM-ddTHH:mm:ss}.");
                SaveToFile();
                return true;
            }
        }

        public bool ChangeSensorStatus(string sensorId, string novoEstado)
        {
            string[] estadosValidos = { "ativo", "manutencao", "desativado", "indisponivel", "desligado" };
            string? estadoLower = novoEstado?.ToLower();

            if (Array.IndexOf(estadosValidos, estadoLower) < 0)
            {
                Log($"[CONFIG] ERRO: Estado inválido '{novoEstado}'. Valores permitidos: {string.Join(", ", estadosValidos)}.");
                return false;
            }

            lock (_lock)
            {
                if (!_sensors.ContainsKey(sensorId))
                {
                    Log($"[CONFIG] AVISO: Sensor '{sensorId}' não encontrado na configuração.");
                    return false;
                }

                string estadoAnterior = _sensors[sensorId].Estado;
                _sensors[sensorId].Estado = estadoLower!;
                Log($"[CONFIG] Sensor '{sensorId}' — estado alterado de '{estadoAnterior}' para '{estadoLower}'.");
                SaveToFile();
                return true;
            }
        }

        public SensorConfig? GetSensor(string sensorId)
        {
            lock (_lock)
            {
                if (_sensors.TryGetValue(sensorId, out SensorConfig? config))
                    return config.Clone();
                return null;
            }
        }

        public List<SensorConfig> GetAllSensors()
        {
            lock (_lock)
            {
                return _sensors.Values.Select(s => s.Clone()).ToList();
            }
        }

        private void SaveToFile()
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("# sensor_id:estado:zona:[tipos_dados]:last_sync");

                foreach (var sensor in _sensors.Values)
                {
                    lines.Add(sensor.ToCsvLine());
                }

                File.WriteAllLines(_filePath, lines);
                Log($"[CONFIG] Ficheiro '{_filePath}' atualizado com sucesso ({_sensors.Count} sensor(es)).");
            }
            catch (Exception ex)
            {
                Log($"[CONFIG] ERRO ao escrever ficheiro '{_filePath}': {ex.Message}");
            }
        }
    }
}
