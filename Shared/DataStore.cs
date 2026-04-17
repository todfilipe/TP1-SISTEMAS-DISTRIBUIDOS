using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;

namespace OneHealth.Shared
{
    public enum ResultadoArmazenamento
    {
        Sucesso,
        DadosInvalidos,
        ErroStorage
    }

    /// <summary>
    /// Armazena medições e estados de sensores numa base de dados SQLite.
    /// Thread-safe via WAL mode; cada operação abre a sua própria ligação.
    /// </summary>
    public class DataStore
    {
        private readonly string _dataDirectory;
        private readonly string _dbPath;
        private readonly string _connectionString;

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
        public string DbPath => _dbPath;

        public DataStore(string? dataDirectory = null)
        {
            _dataDirectory = dataDirectory
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "data");
            _dataDirectory = Path.GetFullPath(_dataDirectory);

            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
                Log($"[DataStore] Diretório de dados criado: {_dataDirectory}");
            }

            _dbPath = Path.Combine(_dataDirectory, "urbano.db");
            _connectionString = $"Data Source={_dbPath}";

            InicializarBaseDados();
            Log($"[DataStore] Base de dados SQLite pronta: {_dbPath}");
        }

        private void Log(string msg)
        {
            OnLogMessage?.Invoke(msg);
            Console.WriteLine(msg);
        }

        private void InicializarBaseDados()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            conn.Execute("PRAGMA journal_mode=WAL;");

            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS medicoes (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    sensor_id TEXT NOT NULL,
                    tipo_dado TEXT NOT NULL,
                    zona TEXT NOT NULL,
                    valor REAL NOT NULL,
                    timestamp TEXT NOT NULL
                );");

            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS sensor_status (
                    sensor_id TEXT PRIMARY KEY,
                    estado TEXT NOT NULL,
                    timestamp TEXT NOT NULL
                );");

            conn.Execute("CREATE INDEX IF NOT EXISTS idx_medicoes_tipo_ts ON medicoes(tipo_dado, timestamp);");
            conn.Execute("CREATE INDEX IF NOT EXISTS idx_medicoes_zona ON medicoes(zona);");
            conn.Execute("CREATE INDEX IF NOT EXISTS idx_medicoes_sensor ON medicoes(sensor_id);");
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

            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                conn.Execute(
                    @"INSERT INTO medicoes (sensor_id, tipo_dado, zona, valor, timestamp)
                      VALUES (@sensorId, @tipoDado, @zona, @valor, @timestamp);",
                    new
                    {
                        sensorId,
                        tipoDado,
                        zona,
                        valor = valorNumerico,
                        timestamp
                    });

                Log($"[DataStore] Medição armazenada: {tipoDado} <- {sensorId},{zona},{valor},{timestamp}");
                return ResultadoArmazenamento.Sucesso;
            }
            catch (SqliteException ex)
            {
                Log($"[DataStore] ERRO SQLite ao armazenar medição: {ex.Message}");
                return ResultadoArmazenamento.ErroStorage;
            }
            catch (Exception ex)
            {
                Log($"[DataStore] ERRO ao armazenar medição: {ex.Message}");
                return ResultadoArmazenamento.ErroStorage;
            }
        }

        public void RegistarEstadoSensor(string sensorId, string estado)
        {
            Log($"[DataStore] Estado do sensor atualizado: {sensorId} -> {estado}");

            string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                conn.Execute(
                    @"INSERT OR REPLACE INTO sensor_status (sensor_id, estado, timestamp)
                      VALUES (@sensorId, @estado, @timestamp);",
                    new { sensorId, estado, timestamp = ts });
            }
            catch (Exception ex)
            {
                Log($"[DataStore] ERRO ao registar estado: {ex.Message}");
            }
        }

        /// <summary>
        /// Lê medições recentes da base de dados com filtros opcionais.
        /// Valores null ou "ALL" em tipo/zona/sensorId significam "sem filtro".
        /// </summary>
        public List<MedicaoEntry> GetRecentData(string? tipo = null, string? zona = null, string? sensorId = null, int limit = 50)
        {
            var result = new List<MedicaoEntry>();

            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                var where = new List<string>();
                var parms = new DynamicParameters();

                if (!string.IsNullOrEmpty(tipo) && tipo != "ALL")
                {
                    where.Add("tipo_dado = @tipo");
                    parms.Add("tipo", tipo);
                }
                if (!string.IsNullOrEmpty(zona) && zona != "ALL")
                {
                    where.Add("zona = @zona");
                    parms.Add("zona", zona);
                }
                if (!string.IsNullOrEmpty(sensorId) && sensorId != "ALL")
                {
                    where.Add("sensor_id = @sensorId");
                    parms.Add("sensorId", sensorId);
                }
                parms.Add("limit", limit);

                string whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
                string sql = $@"
                    SELECT sensor_id AS SensorId, zona AS Zona, tipo_dado AS Tipo,
                           valor AS Valor, timestamp AS Timestamp
                    FROM medicoes
                    {whereClause}
                    ORDER BY timestamp DESC
                    LIMIT @limit;";

                var rows = conn.Query(sql, parms);
                foreach (var row in rows)
                {
                    result.Add(new MedicaoEntry
                    {
                        SensorId = row.SensorId ?? "",
                        Zona = row.Zona ?? "",
                        Tipo = row.Tipo ?? "",
                        Valor = Convert.ToString(row.Valor, System.Globalization.CultureInfo.InvariantCulture) ?? "",
                        Timestamp = row.Timestamp ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                Log($"[DataStore] ERRO GetRecentData: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Devolve o estado atual de cada sensor registado.
        /// </summary>
        public List<(string SensorId, string Estado, string Timestamp)> GetSensorStatuses()
        {
            var result = new List<(string, string, string)>();

            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                var rows = conn.Query(
                    "SELECT sensor_id AS SensorId, estado AS Estado, timestamp AS Timestamp FROM sensor_status ORDER BY sensor_id;");

                foreach (var row in rows)
                {
                    result.Add(((string)row.SensorId, (string)row.Estado, (string)row.Timestamp));
                }
            }
            catch (Exception ex)
            {
                Log($"[DataStore] ERRO GetSensorStatuses: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Devolve a contagem de medições por tipo de dado.
        /// Substitui a antiga enumeração de ficheiros CSV.
        /// </summary>
        public List<(string Tipo, int Count)> GetDataCounts()
        {
            var result = new List<(string, int)>();

            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                var rows = conn.Query(
                    "SELECT tipo_dado AS Tipo, COUNT(*) AS N FROM medicoes GROUP BY tipo_dado ORDER BY tipo_dado;");

                foreach (var row in rows)
                {
                    result.Add(((string)row.Tipo, Convert.ToInt32(row.N)));
                }
            }
            catch (Exception ex)
            {
                Log($"[DataStore] ERRO GetDataCounts: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Total global de medições armazenadas.
        /// </summary>
        public int GetTotalRecords()
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM medicoes;");
            }
            catch (Exception ex)
            {
                Log($"[DataStore] ERRO GetTotalRecords: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Tamanho do ficheiro da base de dados (em bytes).
        /// </summary>
        public long GetDatabaseSizeBytes()
        {
            try
            {
                var info = new FileInfo(_dbPath);
                return info.Exists ? info.Length : 0;
            }
            catch { return 0; }
        }
    }
}
