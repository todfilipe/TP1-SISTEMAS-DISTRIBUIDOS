using Dapper;
using Microsoft.Data.Sqlite;
using Shared;

namespace Servidor
{
    public enum ResultadoArmazenamento
    {
        Sucesso,
        DadosInvalidos,
        ErroStorage
    }

    public sealed class PendingReadingRecord
    {
        public long Id { get; set; }
        public string? MessageId { get; set; }
        public string SensorId { get; set; } = string.Empty;
        public string TipoDado { get; set; } = string.Empty;
        public string Zona { get; set; } = string.Empty;
        public string Valor { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string GatewayId { get; set; } = string.Empty;
        public string OriginalMessage { get; set; } = string.Empty;
        public int SyncAttempts { get; set; }
    }

    /// <summary>
    /// SQLite local usado para estado local legado e como fila de fallback quando o MongoDB falha.
    /// </summary>
    public class DataStore
    {
        private readonly string _dataDirectory;
        private readonly string _connectionString;

        public DataStore(string? dataDirectory = null)
        {
            _dataDirectory = dataDirectory
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "data");
            _dataDirectory = Path.GetFullPath(_dataDirectory);

            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
                Console.WriteLine($"[DataStore] Diretorio de dados criado: {_dataDirectory}");
            }

            string dbPath = Path.Combine(_dataDirectory, "urbano.db");
            _connectionString = $"Data Source={dbPath}";

            InicializarBaseDados();
            Console.WriteLine($"[DataStore] Base de dados SQLite pronta: {dbPath}");
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

            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS pending_readings (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    message_id TEXT NULL,
                    sensor_id TEXT NOT NULL,
                    tipo_dado TEXT NOT NULL,
                    zona TEXT NOT NULL,
                    valor TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    gateway_id TEXT NOT NULL,
                    original_message TEXT NOT NULL,
                    synced INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    last_sync_attempt_at TEXT NULL,
                    sync_attempts INTEGER NOT NULL DEFAULT 0
                );");

            conn.Execute(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ux_pending_readings_message_id
                ON pending_readings(message_id)
                WHERE message_id IS NOT NULL AND message_id <> '';");
        }

        public ResultadoArmazenamento ValidarMedicao(
            string sensorId,
            string tipoDado,
            string valor,
            string zona,
            string timestamp,
            out double valorNumerico)
        {
            valorNumerico = 0;

            if (string.IsNullOrWhiteSpace(sensorId))
            {
                Console.WriteLine("[DataStore] Sensor ID vazio.");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            if (!ProtocolConstants.IsValidSensorType(tipoDado))
            {
                Console.WriteLine($"[DataStore] Tipo de dado invalido: {tipoDado}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            if (!ProtocolConstants.IsValidZone(zona))
            {
                Console.WriteLine($"[DataStore] Zona invalida: {zona}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            if (!double.TryParse(
                    valor,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out valorNumerico))
            {
                Console.WriteLine($"[DataStore] Valor numerico invalido: {valor}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            if (valorNumerico < 0 && tipoDado != "TEMP")
            {
                Console.WriteLine($"[DataStore] Valor negativo rejeitado para {tipoDado}: {valor}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            if (!DateTime.TryParse(
                    timestamp,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime ts))
            {
                Console.WriteLine($"[DataStore] Timestamp invalido: {timestamp}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            if (ts > DateTime.UtcNow.AddSeconds(60))
            {
                Console.WriteLine($"[DataStore] Timestamp demasiado no futuro: {timestamp}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            return ResultadoArmazenamento.Sucesso;
        }

        /// <summary>
        /// Escrita legada TP1. O fluxo principal ja nao usa esta tabela para leituras novas.
        /// </summary>
        public ResultadoArmazenamento ArmazenarMedicao(string sensorId, string tipoDado, string valor, string zona, string timestamp)
        {
            ResultadoArmazenamento validacao = ValidarMedicao(sensorId, tipoDado, valor, zona, timestamp, out double valorNumerico);
            if (validacao != ResultadoArmazenamento.Sucesso)
            {
                return validacao;
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

                Console.WriteLine($"[DataStore] Medicao armazenada: {tipoDado} <- {sensorId},{zona},{valor},{timestamp}");
                return ResultadoArmazenamento.Sucesso;
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"[DataStore] ERRO SQLite ao armazenar medicao: {ex.Message}");
                return ResultadoArmazenamento.ErroStorage;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataStore] ERRO ao armazenar medicao: {ex.Message}");
                return ResultadoArmazenamento.ErroStorage;
            }
        }

        public ResultadoArmazenamento ArmazenarLeituraPendente(
            string? messageId,
            string sensorId,
            string tipoDado,
            string valor,
            string zona,
            string timestamp,
            string gatewayId,
            string originalMessage)
        {
            ResultadoArmazenamento validacao = ValidarMedicao(sensorId, tipoDado, valor, zona, timestamp, out _);
            if (validacao != ResultadoArmazenamento.Sucesso)
            {
                return validacao;
            }

            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                int rows = conn.Execute(
                    @"INSERT OR IGNORE INTO pending_readings
                        (message_id, sensor_id, tipo_dado, zona, valor, timestamp, gateway_id, original_message, synced, created_at, sync_attempts)
                      VALUES
                        (@messageId, @sensorId, @tipoDado, @zona, @valor, @timestamp, @gatewayId, @originalMessage, 0, @createdAt, 0);",
                    new
                    {
                        messageId = string.IsNullOrWhiteSpace(messageId) ? null : messageId,
                        sensorId,
                        tipoDado,
                        zona,
                        valor,
                        timestamp,
                        gatewayId,
                        originalMessage,
                        createdAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                    });

                if (rows == 0)
                {
                    Console.WriteLine($"[DataStore] Leitura pendente ja existia no fallback local: messageId={messageId}");
                }
                else
                {
                    Console.WriteLine($"[DataStore] Leitura guardada no fallback local pending_readings: {tipoDado} <- {sensorId},{zona},{valor},{timestamp}");
                }

                return ResultadoArmazenamento.Sucesso;
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"[DataStore] ERRO SQLite ao guardar leitura pendente: {ex.Message}");
                return ResultadoArmazenamento.ErroStorage;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataStore] ERRO ao guardar leitura pendente: {ex.Message}");
                return ResultadoArmazenamento.ErroStorage;
            }
        }

        public IReadOnlyList<PendingReadingRecord> ObterLeiturasPendentes(int limit = 100)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                return conn.Query<PendingReadingRecord>(
                    @"SELECT
                        id AS Id,
                        message_id AS MessageId,
                        sensor_id AS SensorId,
                        tipo_dado AS TipoDado,
                        zona AS Zona,
                        valor AS Valor,
                        timestamp AS Timestamp,
                        gateway_id AS GatewayId,
                        original_message AS OriginalMessage,
                        sync_attempts AS SyncAttempts
                      FROM pending_readings
                      WHERE synced = 0
                      ORDER BY id ASC
                      LIMIT @limit;",
                    new { limit }).AsList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataStore] ERRO ao consultar leituras pendentes: {ex.Message}");
                return Array.Empty<PendingReadingRecord>();
            }
        }

        public void MarcarLeituraPendenteSincronizada(long id)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                conn.Execute(
                    @"UPDATE pending_readings
                      SET synced = 1, last_sync_attempt_at = @attemptAt
                      WHERE id = @id;",
                    new { id, attemptAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss") });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataStore] ERRO ao marcar leitura pendente como sincronizada: {ex.Message}");
            }
        }

        public void RegistarTentativaSincronizacao(long id)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                conn.Execute(
                    @"UPDATE pending_readings
                      SET sync_attempts = sync_attempts + 1, last_sync_attempt_at = @attemptAt
                      WHERE id = @id;",
                    new { id, attemptAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss") });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataStore] ERRO ao registar tentativa de sincronizacao: {ex.Message}");
            }
        }

        public void RegistarEstadoSensor(string sensorId, string estado)
        {
            Console.WriteLine($"[DataStore] Estado do sensor atualizado: {sensorId} -> {estado}");
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
                Console.WriteLine($"[DataStore] ERRO ao registar estado: {ex.Message}");
            }
        }
    }
}
