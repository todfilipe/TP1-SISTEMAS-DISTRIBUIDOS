using System;
using System.Collections.Generic;
using System.IO;

namespace OneHealthMonitor.Core
{
    public enum ResultadoArmazenamento
    {
        Sucesso,
        DadosInvalidos,
        ErroStorage
    }

    /// <summary>
    /// Armazena medições ambientais em ficheiros CSV separados por tipo de dado.
    /// Thread-safe.
    /// </summary>
    public class DataStore
    {
        private readonly string _dataDirectory;
        private readonly Dictionary<string, object> _fileLocks = new();
        private readonly object _dictLock = new();

        private static readonly HashSet<string> TiposValidos = new()
        {
            "TEMP", "HUM", "AR", "RUIDO", "PM2.5", "PM10", "LUZ", "VIDEO"
        };

        private static readonly HashSet<string> ZonasValidas = new()
        {
            "ZONA_CENTRO", "ZONA_ESCOLAR", "ZONA_INDUSTRIAL",
            "ZONA_RESIDENCIAL", "ZONA_PARQUE"
        };

        public event Action<string>? OnLogMessage;

        public string DataDirectory => _dataDirectory;

        public DataStore(string? dataDirectory = null)
        {
            _dataDirectory = dataDirectory
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "data");
            _dataDirectory = Path.GetFullPath(_dataDirectory);

            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
                OnLogMessage?.Invoke($"[DataStore] Diretório de dados criado: {_dataDirectory}");
            }
        }

        private void Log(string msg) => OnLogMessage?.Invoke(msg);

        private object GetLockForType(string tipoDado)
        {
            lock (_dictLock)
            {
                if (!_fileLocks.ContainsKey(tipoDado))
                    _fileLocks[tipoDado] = new object();
                return _fileLocks[tipoDado];
            }
        }

        public ResultadoArmazenamento ArmazenarMedicao(string sensorId, string tipoDado, string valor, string zona, string timestamp)
        {
            if (!TiposValidos.Contains(tipoDado))
            {
                Log($"[DataStore] Tipo de dado inválido: {tipoDado}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            if (!ZonasValidas.Contains(zona))
            {
                Log($"[DataStore] Zona inválida: {zona}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            if (!double.TryParse(valor, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double valorNumerico))
            {
                Log($"[DataStore] Valor numérico inválido: {valor}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            if (valorNumerico < 0 && tipoDado != "TEMP")
            {
                Log($"[DataStore] Valor negativo rejeitado para {tipoDado}: {valor}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            if (!DateTime.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime ts))
            {
                Log($"[DataStore] Timestamp inválido: {timestamp}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            if (ts > DateTime.UtcNow.AddSeconds(60))
            {
                Log($"[DataStore] Timestamp demasiado no futuro: {timestamp}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            string linha = $"{sensorId},{zona},{valor},{timestamp}";
            string filePath = Path.Combine(_dataDirectory, $"{tipoDado}.csv");

            object fileLock = GetLockForType(tipoDado);
            try
            {
                lock (fileLock)
                {
                    if (!File.Exists(filePath))
                    {
                        File.WriteAllText(filePath, "sensor_id,zona,valor,timestamp\n");
                    }
                    File.AppendAllText(filePath, linha + "\n");
                }

                Log($"[DataStore] Medição armazenada: {tipoDado} <- {linha}");
                return ResultadoArmazenamento.Sucesso;
            }
            catch (Exception ex)
            {
                Log($"[DataStore] ERRO ao escrever ficheiro {filePath}: {ex.Message}");
                return ResultadoArmazenamento.ErroStorage;
            }
        }

        public void RegistarEstadoSensor(string sensorId, string estado)
        {
            Log($"[DataStore] Estado do sensor atualizado: {sensorId} -> {estado}");

            string filePath = Path.Combine(_dataDirectory, "sensor_status.csv");
            string novaLinha = $"{sensorId},{estado},{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss}";

            object fileLock = GetLockForType("sensor_status");
            try
            {
                lock (fileLock)
                {
                    var estadosAtuais = new Dictionary<string, string>();

                    if (File.Exists(filePath))
                    {
                        string[] linhas = File.ReadAllLines(filePath);
                        for (int i = 1; i < linhas.Length; i++)
                        {
                            if (string.IsNullOrWhiteSpace(linhas[i])) continue;
                            string[] partes = linhas[i].Split(',');
                            if (partes.Length > 0)
                                estadosAtuais[partes[0]] = linhas[i];
                        }
                    }

                    estadosAtuais[sensorId] = novaLinha;

                    List<string> paraGravar = new List<string>();
                    paraGravar.Add("sensor_id,estado,timestamp");
                    paraGravar.AddRange(estadosAtuais.Values);

                    File.WriteAllLines(filePath, paraGravar);
                }
            }
            catch (Exception ex)
            {
                Log($"[DataStore] ERRO ao registar estado: {ex.Message}");
            }
        }

        /// <summary>
        /// Lê as medições recentes dos ficheiros CSV, com filtro opcional.
        /// </summary>
        public List<MedicaoEntry> GetRecentData(string? tipo = null, string? zona = null, string? sensorId = null, int limit = 50)
        {
            var result = new List<MedicaoEntry>();

            try
            {
                if (!Directory.Exists(_dataDirectory)) return result;

                string[] files;
                if (tipo != null && tipo != "ALL")
                {
                    string path = Path.Combine(_dataDirectory, $"{tipo}.csv");
                    files = File.Exists(path) ? new[] { path } : Array.Empty<string>();
                }
                else
                {
                    files = Directory.GetFiles(_dataDirectory, "*.csv");
                }

                foreach (string file in files)
                {
                    string filename = Path.GetFileNameWithoutExtension(file);
                    if (filename == "sensor_status") continue;

                    try
                    {
                        string[] lines = File.ReadAllLines(file);
                        for (int i = lines.Length - 1; i >= 1; i--)
                        {
                            if (string.IsNullOrWhiteSpace(lines[i])) continue;
                            string[] parts = lines[i].Split(',');
                            if (parts.Length < 4) continue;

                            if (zona != null && zona != "ALL" && parts[1] != zona) continue;
                            if (sensorId != null && sensorId != "ALL" && parts[0] != sensorId) continue;

                            result.Add(new MedicaoEntry
                            {
                                SensorId = parts[0],
                                Zona = parts[1],
                                Tipo = filename,
                                Valor = parts[2],
                                Timestamp = parts[3]
                            });
                        }
                    }
                    catch { }
                }

                result.Sort((a, b) => string.Compare(b.Timestamp, a.Timestamp, StringComparison.Ordinal));
                if (result.Count > limit)
                    result = result.GetRange(0, limit);
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Retorna informações sobre os ficheiros CSV na pasta de dados.
        /// </summary>
        public List<(string Name, long SizeBytes, int LineCount)> GetDataFiles()
        {
            var result = new List<(string, long, int)>();
            try
            {
                if (!Directory.Exists(_dataDirectory)) return result;

                foreach (string file in Directory.GetFiles(_dataDirectory, "*.csv"))
                {
                    var info = new FileInfo(file);
                    int lineCount = File.ReadAllLines(file).Length - 1; // minus header
                    result.Add((info.Name, info.Length, Math.Max(0, lineCount)));
                }
            }
            catch { }
            return result;
        }

        /// <summary>
        /// Lê os estados dos sensores a partir de sensor_status.csv.
        /// </summary>
        public List<(string SensorId, string Estado, string Timestamp)> GetSensorStatuses()
        {
            var result = new List<(string, string, string)>();
            string filePath = Path.Combine(_dataDirectory, "sensor_status.csv");

            try
            {
                if (!File.Exists(filePath)) return result;

                string[] lines = File.ReadAllLines(filePath);
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    string[] parts = lines[i].Split(',');
                    if (parts.Length >= 3)
                        result.Add((parts[0], parts[1], parts[2]));
                }
            }
            catch { }

            return result;
        }
    }
}
