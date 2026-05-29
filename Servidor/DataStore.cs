using System;
using System.Collections.Generic;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using Shared;

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
    /// Armazena medições ambientais e estados de sensores numa base de dados SQLite.
    /// Thread-safe: usa WAL mode para concorrência nativa; cada operação abre a sua própria ligação.
    /// </summary>
    public class DataStore
    {
        private readonly string _dataDirectory;
        private readonly string _connectionString;
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

            string dbPath = Path.Combine(_dataDirectory, "urbano.db");
            _connectionString = $"Data Source={dbPath}";

            InicializarBaseDados();
            Console.WriteLine($"[DataStore] Base de dados SQLite pronta: {dbPath}");
        }

        /// <summary>
        /// Cria as tabelas se não existirem e ativa o modo WAL para concorrência.
        /// </summary>
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
        }

        /// <summary>
        /// Armazena uma medição ambiental na tabela medicoes após validação.
        /// </summary>
        /// <returns>ResultadoArmazenamento indicando sucesso, dados inválidos ou erro de storage.</returns>
        public ResultadoArmazenamento ArmazenarMedicao(string sensorId, string tipoDado, string valor, string zona, string timestamp)
        {
            // Validar tipo de dado
            if (!ProtocolConstants.IsValidSensorType(tipoDado))
            {
                Console.WriteLine($"[DataStore] Tipo de dado inválido: {tipoDado}");
                return ResultadoArmazenamento.DadosInvalidos;
            }

            // Validar zona
            if (!ProtocolConstants.IsValidZone(zona))
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

                Console.WriteLine($"[DataStore] Medição armazenada: {tipoDado} <- {sensorId},{zona},{valor},{timestamp}");
                return ResultadoArmazenamento.Sucesso;
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"[DataStore] ERRO SQLite ao armazenar medição: {ex.Message}");
                return ResultadoArmazenamento.ErroStorage;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataStore] ERRO ao armazenar medição: {ex.Message}");
                return ResultadoArmazenamento.ErroStorage;
            }
        }

        /// <summary>
        /// Regista uma alteração de estado de um sensor.
        /// INSERT OR REPLACE garante um único registo por sensor_id.
        /// </summary>
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
