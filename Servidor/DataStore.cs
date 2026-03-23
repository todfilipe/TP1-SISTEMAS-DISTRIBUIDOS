using System;
using System.Collections.Generic;
using System.IO;

namespace Servidor
{
    /// <summary>
    /// Resultado de uma operação de armazenamento.
    /// Permite distinguir entre sucesso, dados inválidos e falha de storage.
    /// </summary>
    public enum ResultadoArmazenamento
    {
        Sucesso,
        DadosInvalidos,
        ErroStorage
    }

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
            // Usar caminho relativo ao executável em vez de depender do working directory
            _dataDirectory = dataDirectory
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "data");

            // Normalizar o caminho para resolver os ".."
            _dataDirectory = Path.GetFullPath(_dataDirectory);

            // Criar diretório de dados se não existir
            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
                Console.WriteLine($"[DataStore] Diretório de dados criado: {_dataDirectory}");
            }
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
        /// <returns>ResultadoArmazenamento indicando sucesso, dados inválidos ou erro de storage.</returns>
        public ResultadoArmazenamento ArmazenarMedicao(string sensorId, string tipoDado, string valor, string zona, string timestamp)
        {
            // Validar tipo de dado
            if (!TiposValidos.Contains(tipoDado))
            {
                Console.WriteLine($"[DataStore] Tipo de dado inválido: {tipoDado}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            // Validar zona
            if (!ZonasValidas.Contains(zona))
            {
                Console.WriteLine($"[DataStore] Zona inválida: {zona}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            // Validar valor numérico
            if (!double.TryParse(valor, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double valorNumerico))
            {
                Console.WriteLine($"[DataStore] Valor numérico inválido: {valor}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            // Valores negativos só são aceites para TEMP
            if (valorNumerico < 0 && tipoDado != "TEMP")
            {
                Console.WriteLine($"[DataStore] Valor negativo rejeitado para {tipoDado}: {valor}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            // Validar timestamp ISO 8601
            if (!DateTime.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime ts))
            {
                Console.WriteLine($"[DataStore] Timestamp inválido: {timestamp}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            // Rejeitar timestamps com mais de 60s no futuro (usar UtcNow para consistência)
            if (ts > DateTime.UtcNow.AddSeconds(60))
            {
                Console.WriteLine($"[DataStore] Timestamp demasiado no futuro: {timestamp}");
                return ResultadoArmazenamento.DadosInvalidos;
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
                return ResultadoArmazenamento.Sucesso;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataStore] ERRO ao escrever ficheiro {filePath}: {ex.Message}");
                return ResultadoArmazenamento.ErroStorage;
            }
        }

        /// <summary>
        /// Regista uma alteração de estado de um sensor (para log/consulta futura).
        /// </summary>
        public void RegistarEstadoSensor(string sensorId, string estado)
        {
            Console.WriteLine($"[DataStore] Estado do sensor atualizado: {sensorId} -> {estado}");

            string filePath = Path.Combine(_dataDirectory, "sensor_status.csv");
            string linha = $"{sensorId},{estado},{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss}";

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
