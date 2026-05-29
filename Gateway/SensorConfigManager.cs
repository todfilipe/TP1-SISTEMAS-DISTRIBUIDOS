using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Shared;

namespace Gateway
{
    /// <summary>
    /// Gestor do ficheiro de configuração sensors.csv.
    /// Mantém um dicionário thread-safe em memória e persiste alterações no ficheiro.
    /// 
    /// Formato CSV: sensor_id:estado:zona:[tipos_dados]:last_sync
    /// Exemplo:     S101:ativo:ZONA_CENTRO:[TEMP,HUM,RUIDO]:2026-03-10T08:45:00
    /// </summary>
    public class SensorConfigManager : IDisposable
    {
        private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(10);

        // Caminho para o ficheiro CSV
        private readonly string _filePath;

        // Dicionário em memória: chave = sensor_id, valor = SensorConfig
        private readonly Dictionary<string, SensorConfig> _sensors;

        // Lock intra-processo para acesso exclusivo ao dicionário e ao ficheiro CSV.
        // O sensors.csv é propriedade de um único Gateway, por isso não é necessária
        // sincronização inter-processo (um Mutex nomeado podia ficar bloqueado para
        // sempre se uma exceção ocorresse antes do release).
        private readonly object _syncObj = new object();
        private readonly Timer _flushTimer;
        private bool _dirty;
        private bool _disposed;

        /// <summary>
        /// Inicializa o gestor com o caminho do ficheiro CSV.
        /// </summary>
        /// <param name="filePath">Caminho absoluto ou relativo para o ficheiro sensors.csv.</param>
        public SensorConfigManager(string filePath)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _sensors = new Dictionary<string, SensorConfig>(StringComparer.OrdinalIgnoreCase);
            _flushTimer = new Timer(_ => FlushPendingChanges(), null, FlushInterval, FlushInterval);
        }

        // ─────────────────────────────────────────────
        //  LoadConfig — Lê o ficheiro CSV para memória
        // ─────────────────────────────────────────────

        /// <summary>
        /// Lê o ficheiro sensors.csv, interpreta cada linha e carrega os dados para o dicionário.
        /// Linhas malformadas são ignoradas e registadas na consola.
        /// </summary>
        /// <returns>Número de sensores carregados com sucesso.</returns>
        public int LoadConfig()
        {
            lock (_syncObj)
            {
                _sensors.Clear();

                // Verifica se o ficheiro existe
                if (!File.Exists(_filePath))
                {
                    Console.WriteLine($"[CONFIG] AVISO: Ficheiro '{_filePath}' não encontrado. A iniciar com configuração vazia.");
                    return 0;
                }

                string[] lines;
                try
                {
                    lines = File.ReadAllLines(_filePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CONFIG] ERRO ao ler ficheiro '{_filePath}': {ex.Message}");
                    return 0;
                }

                int loaded = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();

                    // Ignorar linhas vazias e comentários (começam com #)
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                        continue;

                    SensorConfig config = ParseLine(line, i + 1);
                    if (config != null)
                    {
                        _sensors[config.SensorId] = config;
                        loaded++;
                    }
                }

                Console.WriteLine($"[CONFIG] Carregados {loaded} sensor(es) a partir de '{_filePath}'.");
                return loaded;
            }
        }

        // ─────────────────────────────────────────────
        //  ParseLine — Interpreta uma linha do CSV
        // ─────────────────────────────────────────────

        /// <summary>
        /// Interpreta uma linha no formato sensor_id:estado:zona:[tipos]:last_sync.
        /// </summary>
        /// <param name="line">Linha do ficheiro.</param>
        /// <param name="lineNumber">Número da linha (para mensagens de erro).</param>
        /// <returns>SensorConfig ou null se a linha for inválida.</returns>
        private SensorConfig ParseLine(string line, int lineNumber)
        {
            // Estratégia: encontrar os [ ] para separar a lista de tipos,
            // porque os tipos estão entre parênteses retos e podem conter vírgulas.

            int openBracket = line.IndexOf('[');
            int closeBracket = line.IndexOf(']');

            if (openBracket < 0 || closeBracket < 0 || closeBracket <= openBracket)
            {
                Console.WriteLine($"[CONFIG] AVISO: Linha {lineNumber} malformada (parênteses retos não encontrados): {line}");
                return null;
            }

            // Parte antes dos [ ] → sensor_id:estado:zona:
            string beforeBrackets = line.Substring(0, openBracket);
            // Conteúdo dentro dos [ ] → TEMP,HUM,RUIDO
            string insideBrackets = line.Substring(openBracket + 1, closeBracket - openBracket - 1);
            // Parte depois dos [ ] → :last_sync
            string afterBrackets = line.Substring(closeBracket + 1);

            // Separar a parte antes dos parênteses por ':'
            // Espera-se: sensor_id : estado : zona : (início do campo tipos, vazio aqui)
            string[] prefixParts = beforeBrackets.Split(':');
            if (prefixParts.Length < 4)
            {
                Console.WriteLine($"[CONFIG] AVISO: Linha {lineNumber} malformada (campos insuficientes antes dos tipos): {line}");
                return null;
            }

            string sensorId = prefixParts[0].Trim();
            string estado = prefixParts[1].Trim();
            string zona = prefixParts[2].Trim();

            // Validar que sensor_id, estado e zona não são vazios
            if (string.IsNullOrEmpty(sensorId) || string.IsNullOrEmpty(estado) || string.IsNullOrEmpty(zona))
            {
                Console.WriteLine($"[CONFIG] AVISO: Linha {lineNumber} tem campos obrigatórios vazios: {line}");
                return null;
            }

            // Validar a zona contra o conjunto de zonas reconhecidas pelo sistema
            if (!DataValidator.ZonasValidas.Contains(zona))
            {
                Console.WriteLine($"[CONFIG] AVISO: Linha {lineNumber} tem zona inválida '{zona}' (não reconhecida): {line}");
                return null;
            }

            // Interpretar a lista de tipos de dados (filtrando os que não são reconhecidos globalmente)
            List<string> tiposDados = new List<string>();
            if (!string.IsNullOrWhiteSpace(insideBrackets))
            {
                string[] tipos = insideBrackets.Split(',');
                foreach (string t in tipos)
                {
                    string trimmed = t.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                        continue;

                    if (!DataValidator.TiposGlobais.Contains(trimmed))
                    {
                        Console.WriteLine($"[CONFIG] AVISO: Linha {lineNumber} — tipo '{trimmed}' não é reconhecido pelo sistema e será ignorado: {line}");
                        continue;
                    }

                    tiposDados.Add(trimmed);
                }
            }

            // Separar a parte depois dos parênteses para obter o timestamp
            // afterBrackets começa com ":" seguido do timestamp
            string[] suffixParts = afterBrackets.Split(':');
            // suffixParts[0] será vazio (antes do primeiro ':')
            // O timestamp ISO 8601 contém ':' (ex: 2026-03-10T08:45:00),
            // então precisamos de juntar tudo a partir do índice 1

            string timestampStr = "";
            if (suffixParts.Length > 1)
            {
                // Juntar com ':' a partir do índice 1 para reconstruir o timestamp
                timestampStr = string.Join(":", suffixParts, 1, suffixParts.Length - 1).Trim();
            }

            // Tentar fazer o parse do timestamp ISO 8601
            DateTime lastSync;
            if (!DateTime.TryParse(timestampStr, CultureInfo.InvariantCulture,
                                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                    out lastSync))
            {
                Console.WriteLine($"[CONFIG] AVISO: Linha {lineNumber} — timestamp inválido '{timestampStr}': {line}");
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

        // ─────────────────────────────────────────────
        //  UpdateLastSync — Atualiza o campo last_sync
        // ─────────────────────────────────────────────

        /// <summary>
        /// Atualiza o campo last_sync de um sensor em memória e agenda a persistência no ficheiro CSV.
        /// </summary>
        /// <param name="sensorId">Identificador do sensor.</param>
        /// <param name="timestamp">Nova data/hora de sincronização.</param>
        /// <returns>true se atualizado com sucesso; false se o sensor não existe.</returns>
        public bool UpdateLastSync(string sensorId, DateTime timestamp)
        {
            lock (_syncObj)
            {
                if (!_sensors.ContainsKey(sensorId))
                {
                    Console.WriteLine($"[CONFIG] AVISO: Sensor '{sensorId}' não encontrado na configuração.");
                    return false;
                }

                _sensors[sensorId].LastSync = timestamp;
                Console.WriteLine($"[CONFIG] Sensor '{sensorId}' — last_sync atualizado para {timestamp:yyyy-MM-ddTHH:mm:ss}.");

                MarkDirty();
                return true;
            }
        }

        // ─────────────────────────────────────────────
        //  ChangeSensorStatus — Altera o estado do sensor
        // ─────────────────────────────────────────────

        /// <summary>
        /// Altera o estado de um sensor (ativo, manutencao, desativado, indisponivel, desligado).
        /// A alteração é persistida no ficheiro CSV pelo flush periódico.
        /// </summary>
        /// <param name="sensorId">Identificador do sensor.</param>
        /// <param name="novoEstado">Novo estado a atribuir.</param>
        /// <returns>true se alterado com sucesso; false se o sensor não existe ou estado inválido.</returns>
        public bool ChangeSensorStatus(string sensorId, string novoEstado)
        {
            // Validar os estados permitidos
            string estadoLower = novoEstado?.ToLower();

            if (!ProtocolConstants.IsValidSensorState(estadoLower))
            {
                Console.WriteLine($"[CONFIG] ERRO: Estado inválido '{novoEstado}'. Valores permitidos: {string.Join(", ", ProtocolConstants.SensorStates)}.");
                return false;
            }

            lock (_syncObj)
            {
                if (!_sensors.ContainsKey(sensorId))
                {
                    Console.WriteLine($"[CONFIG] AVISO: Sensor '{sensorId}' não encontrado na configuração.");
                    return false;
                }

                string estadoAnterior = _sensors[sensorId].Estado;
                _sensors[sensorId].Estado = estadoLower;
                Console.WriteLine($"[CONFIG] Sensor '{sensorId}' — estado alterado de '{estadoAnterior}' para '{estadoLower}'.");

                MarkDirty();
                return true;
            }
        }

        // ─────────────────────────────────────────────
        //  GetSensor — Retorna os dados de um sensor
        // ─────────────────────────────────────────────

        /// <summary>
        /// Retorna o objeto SensorConfig de um sensor específico para validação.
        /// </summary>
        /// <param name="sensorId">Identificador do sensor.</param>
        /// <returns>SensorConfig ou null se não encontrado.</returns>
        public SensorConfig GetSensor(string sensorId)
        {
            lock (_syncObj)
            {
                if (_sensors.TryGetValue(sensorId, out SensorConfig config))
                    return config.Clone();   // snapshot thread-safe

                return null;
            }
        }

        // ─────────────────────────────────────────────
        //  GetDicionarioParaIteracao — Retorna dicionário para iteração
        // ─────────────────────────────────────────────

        /// <summary>
        /// Retorna uma cópia do dicionário de todos os sensores carregados, de forma thread-safe.
        /// </summary>
        public Dictionary<string, SensorConfig> GetDicionarioParaIteracao()
        {
            lock (_syncObj)
            {
                return _sensors.ToDictionary(entry => entry.Key, entry => entry.Value.Clone(), StringComparer.OrdinalIgnoreCase);
            }
        }

        // ─────────────────────────────────────────────
        //  GetAllSensors — Lista todos os sensores
        // ─────────────────────────────────────────────

        /// <summary>
        /// Retorna uma cópia da lista de todos os sensores carregados.
        /// </summary>
        public List<SensorConfig> GetAllSensors()
        {
            lock (_syncObj)
            {
                return _sensors.Values.Select(s => s.Clone()).ToList();
            }
        }

        // ─────────────────────────────────────────────
        //  SaveToFile — Persiste o estado em ficheiro
        // ─────────────────────────────────────────────

        /// <summary>
        /// Marca a configuração em memória como pendente de persistência.
        /// </summary>
        private void MarkDirty()
        {
            _dirty = true;
        }

        public void FlushPendingChanges()
        {
            lock (_syncObj)
            {
                if (!_dirty)
                {
                    return;
                }

                if (SaveToFile())
                {
                    _dirty = false;
                }
            }
        }

        private bool SaveToFile()
        {
            try
            {
                // Construir todas as linhas a partir do dicionário
                List<string> lines = new List<string>();

                // Cabeçalho comentado (opcional, para legibilidade do ficheiro)
                lines.Add("# sensor_id:estado:zona:[tipos_dados]:last_sync");

                foreach (var sensor in _sensors.Values)
                {
                    lines.Add(sensor.ToCsvLine());
                }

                File.WriteAllLines(_filePath, lines);
                Console.WriteLine($"[CONFIG] Ficheiro '{_filePath}' atualizado com sucesso ({_sensors.Count} sensor(es)).");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CONFIG] ERRO ao escrever ficheiro '{_filePath}': {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _flushTimer.Dispose();
            FlushPendingChanges();
            _disposed = true;
        }
    }
}
