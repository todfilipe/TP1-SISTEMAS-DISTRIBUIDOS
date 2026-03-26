using System;
using System.Collections.Generic;
using System.IO;

namespace Servidor
{
    /// <summary>
    /// Armazena medições ambientais em ficheiros CSV separados por tipo de dado.
    /// Cada tipo de dado (TEMP, HUM, PM2.5, etc.) tem o seu próprio ficheiro.
    /// Thread-safe: usa locks por ficheiro para proteger escritas concorrentes.
    /// </summary>
    public class DataStore
    {
        private readonly string _dataDirectory;

        // Lock por tipo de dado para escritas concorrentes seguras
        private readonly Dictionary<string, object> _fileLocks = new();
        private readonly object _dictLock = new(); // Protege o dicionário de locks

        // Tipos de dados válidos definidos no protocolo
        private static readonly HashSet<string> TiposValidos = new()
        {
            "TEMP", "HUM", "AR", "RUIDO", "PM2.5", "PM10", "LUZ", "VIDEO"
        };

        // Zonas válidas definidas no protocolo
        private static readonly HashSet<string> ZonasValidas = new()
        {
            "ZONA_CENTRO", "ZONA_ESCOLAR", "ZONA_INDUSTRIAL",
            "ZONA_RESIDENCIAL", "ZONA_PARQUE"
        };

        public DataStore(string? dataDirectory = null)
        {
            if (dataDirectory != null)
            {
                _dataDirectory = dataDirectory;
            }
            else
            {
                // Calcular caminho relativo à raiz do projeto (pasta que contém Servidor/)
                // AppContext.BaseDirectory aponta para bin/Debug/net8.0/
                // Subimos 4 níveis: net8.0 -> Debug -> bin -> Servidor -> raiz
                string baseDir = AppContext.BaseDirectory;
                string projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
                _dataDirectory = Path.Combine(projectRoot, "data");
            }

            // Criar diretório de dados se não existir
            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
                Console.WriteLine($"[DataStore] Diretório de dados criado: {_dataDirectory}");
            }

            Console.WriteLine($"[DataStore] Diretório de dados: {Path.GetFullPath(_dataDirectory)}");
        }

        /// <summary>
        /// Obtém ou cria um lock para um dado tipo de dado.
        /// </summary>
        private object GetLockForType(string tipoDado)
        {
            lock (_dictLock)
            {
                if (!_fileLocks.ContainsKey(tipoDado))
                {
                    _fileLocks[tipoDado] = new object();
                }
                return _fileLocks[tipoDado];
            }
        }

        /// <summary>
        /// Armazena uma medição ambiental no ficheiro CSV correspondente ao tipo de dado.
        /// Formato da linha: sensor_id,zona,valor,timestamp
        /// </summary>
        /// <returns>null se armazenado com sucesso, ou código de erro (ERR_INVALID_DATA, ERR_STORAGE_FULL).</returns>
        public string? ArmazenarMedicao(string sensorId, string tipoDado, string valor, string zona, string timestamp)
        {
            // Validar tipo de dado
            if (!TiposValidos.Contains(tipoDado))
            {
                Console.WriteLine($"[DataStore] Tipo de dado inválido: {tipoDado}");
                return "ERR_INVALID_DATA";
            }

            // Validar zona
            if (!ZonasValidas.Contains(zona))
            {
                Console.WriteLine($"[DataStore] Zona inválida: {zona}");
                return "ERR_INVALID_DATA";
            }

            // Validar valor numérico
            if (!double.TryParse(valor, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double valorNumerico))
            {
                Console.WriteLine($"[DataStore] Valor numérico inválido: {valor}");
                return "ERR_INVALID_DATA";
            }

            // Valores negativos só são aceites para TEMP
            if (valorNumerico < 0 && tipoDado != "TEMP")
            {
                Console.WriteLine($"[DataStore] Valor negativo rejeitado para {tipoDado}: {valor}");
                return "ERR_INVALID_DATA";
            }

            // Validar timestamp ISO 8601
            if (!DateTime.TryParse(timestamp, out DateTime ts))
            {
                Console.WriteLine($"[DataStore] Timestamp inválido: {timestamp}");
                return "ERR_INVALID_DATA";
            }

            // Rejeitar timestamps com mais de 60s no futuro
            if (ts > DateTime.Now.AddSeconds(60))
            {
                Console.WriteLine($"[DataStore] Timestamp demasiado no futuro: {timestamp}");
                return "ERR_INVALID_DATA";
            }

            // Construir linha CSV
            string linha = $"{sensorId},{zona},{valor},{timestamp}";
            string filePath = Path.Combine(_dataDirectory, $"{tipoDado}.csv");

            // Escrever no ficheiro com lock por tipo de dado
            object fileLock = GetLockForType(tipoDado);
            try
            {
                lock (fileLock)
                {
                    // Criar ficheiro com cabeçalho se não existir
                    if (!File.Exists(filePath))
                    {
                        File.WriteAllText(filePath, "sensor_id,zona,valor,timestamp\n");
                    }
                    File.AppendAllText(filePath, linha + "\n");
                }

                Console.WriteLine($"[DataStore] Medição armazenada: {tipoDado} <- {linha}");
                return null; // Sucesso
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[DataStore] ERRO I/O ao escrever ficheiro {filePath}: {ex.Message}");
                return "ERR_STORAGE_FULL";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataStore] ERRO ao escrever ficheiro {filePath}: {ex.Message}");
                return "ERR_STORAGE_FULL";
            }
        }

        /// <summary>
        /// Regista uma alteração de estado de um sensor (para log/consulta futura).
        /// </summary>
        public void RegistarEstadoSensor(string sensorId, string estado)
        {
            Console.WriteLine($"[DataStore] Estado do sensor atualizado: {sensorId} -> {estado}");

            string filePath = Path.Combine(_dataDirectory, "sensor_status.csv");
            string linha = $"{sensorId},{estado},{DateTime.Now:yyyy-MM-ddTHH:mm:ss}";

            object fileLock = GetLockForType("sensor_status");
            try
            {
                lock (fileLock)
                {
                    if (!File.Exists(filePath))
                    {
                        File.WriteAllText(filePath, "sensor_id,estado,timestamp\n");
                    }
                    File.AppendAllText(filePath, linha + "\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataStore] ERRO ao registar estado: {ex.Message}");
            }
        }
    }
}
