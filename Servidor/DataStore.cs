using System;
using System.Collections.Generic;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;

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
        /// Representa uma medição lida do store interno do Servidor.
        /// Usada para alimentar os pedidos gRPC ao serviço de Análise.
        /// </summary>
        public record Medicao(double Valor, string Timestamp, string SensorId, string Zona, string Tipo);

        /// <summary>
        /// Obtém medições do store interno (SQLite do Servidor) aplicando filtros.
        /// O Servidor é o dono destes dados; recolhe-os aqui e envia-os ao serviço
        /// de Análise no próprio request (o serviço de Análise não conhece a BD).
        /// Na Fase 3, a fonte passará a ser MongoDB sem alterar a assinatura.
        /// </summary>
        public List<Medicao> ObterMedicoes(string? tipo = null, string? zona = null,
            string? sensorId = null, string? dateFrom = null, string? dateTo = null)
        {
            var resultado = new List<Medicao>();
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                var sql = new System.Text.StringBuilder(
                    "SELECT valor, timestamp, sensor_id, zona, tipo_dado FROM medicoes WHERE 1=1");
                var p = new DynamicParameters();

                if (!string.IsNullOrWhiteSpace(tipo)) { sql.Append(" AND UPPER(tipo_dado) = @tipo"); p.Add("tipo", tipo.Trim().ToUpperInvariant()); }
                if (!string.IsNullOrWhiteSpace(zona)) { sql.Append(" AND UPPER(zona) = @zona"); p.Add("zona", zona.Trim().ToUpperInvariant()); }
                if (!string.IsNullOrWhiteSpace(sensorId)) { sql.Append(" AND UPPER(sensor_id) = @sid"); p.Add("sid", sensorId.Trim().ToUpperInvariant()); }
                if (!string.IsNullOrWhiteSpace(dateFrom)) { sql.Append(" AND timestamp >= @from"); p.Add("from", dateFrom.Trim()); }
                if (!string.IsNullOrWhiteSpace(dateTo)) { sql.Append(" AND timestamp <= @to"); p.Add("to", dateTo.Trim()); }
                sql.Append(" ORDER BY timestamp ASC");

                foreach (var row in conn.Query(sql.ToString(), p))
                {
                    resultado.Add(new Medicao(
                        Convert.ToDouble(row.valor),
                        (string)(row.timestamp ?? ""),
                        (string)(row.sensor_id ?? ""),
                        (string)(row.zona ?? ""),
                        (string)(row.tipo_dado ?? "")));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataStore] ERRO ao obter medições: {ex.Message}");
            }
            return resultado;
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
