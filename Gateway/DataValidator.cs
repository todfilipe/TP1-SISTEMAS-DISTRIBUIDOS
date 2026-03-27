using System;
using System.Collections.Generic;
using System.Globalization;

namespace Gateway
{
    /// <summary>
    /// Resultado da validação de uma mensagem DATA.
    /// Contém o código de erro (ou null se sucesso) e a mensagem FORWARD preparada.
    /// </summary>
    public class DataValidationResult
    {
        /// <summary>Se true, todas as validações passaram.</summary>
        public bool IsValid { get; set; }

        /// <summary>Código de erro a devolver ao sensor (ex: ERR_NOT_REGISTERED). Null se válido.</summary>
        public string ErrorCode { get; set; }

        /// <summary>Mensagem FORWARD pronta a enviar ao servidor. Null se inválido.</summary>
        public string ForwardMessage { get; set; }

        /// <summary>Descrição legível do resultado (para logging).</summary>
        public string LogMessage { get; set; }
    }

    /// <summary>
    /// Validador completo de mensagens DATA enviadas pelos sensores (Fase 3).
    /// 
    /// Ordem estrita de validação:
    ///   1. Registo   → sensor_id existe no CSV?
    ///   2. Estado    → sensor está "ativo"?
    ///   3. Tipo      → tipo_dado pertence à lista autorizada do sensor?
    ///   4. Conteúdo  → formato, timestamp, zona, valor, tipo definido global
    ///   5. Sucesso   → atualiza last_sync + prepara FORWARD
    /// </summary>
    public static class DataValidator
    {
        // ─────────────────────────────────────────────────────────
        //  Constantes de validação
        // ─────────────────────────────────────────────────────────

        /// <summary>Zonas geográficas válidas no sistema.</summary>
        private static readonly HashSet<string> ZonasValidas = new HashSet<string>(StringComparer.Ordinal)
        {
            "ZONA_CENTRO",
            "ZONA_ESCOLAR",
            "ZONA_INDUSTRIAL",
            "ZONA_RESIDENCIAL",
            "ZONA_PARQUE"
        };

        /// <summary>Tipos de dados globais reconhecidos pelo sistema.</summary>
        private static readonly HashSet<string> TiposGlobais = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TEMP", "HUM", "AR", "RUIDO", "PM2.5", "PM10", "LUZ", "VIDEO"
        };

        /// <summary>Tolerância máxima para timestamps no futuro (em segundos).</summary>
        private const int MAX_FUTURE_SECONDS = 60;

        // ─────────────────────────────────────────────────────────
        //  Método principal: ValidateAndProcessData
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Valida uma mensagem DATA completa seguindo rigorosamente a ordem definida
        /// no enunciado da Fase 3:
        ///   1. Registo  →  2. Estado  →  3. Tipo  →  4. Conteúdo  →  5. Sucesso
        /// 
        /// Se todas as validações passarem, atualiza o last_sync do sensor em memória
        /// (via configManager) e constrói a mensagem FORWARD para o servidor.
        /// </summary>
        /// <param name="rawMessage">Linha completa recebida do sensor (ex: "DATA TEMP 22.5 ZONA_CENTRO 2026-03-10T09:15:00").</param>
        /// <param name="sensorId">ID do sensor que enviou a mensagem (obtido no CONNECT).</param>
        /// <param name="configManager">Referência ao gestor de configuração (acesso thread-safe ao dicionário de sensores).</param>
        /// <returns>DataValidationResult com o resultado da validação.</returns>
        public static DataValidationResult ValidateAndProcessData(
            string rawMessage,
            string sensorId,
            SensorConfigManager configManager)
        {
            // ═══════════════════════════════════════════════════════
            //  PASSO 1 — Validação de Registo
            //  Verificar se o sensor_id existe no dicionário CSV
            // ═══════════════════════════════════════════════════════

            SensorConfig sensor = configManager.GetSensor(sensorId);

            if (sensor == null)
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_NOT_REGISTERED",
                    LogMessage = $"[VALIDAÇÃO] Sensor '{sensorId}' não está registado no CSV."
                };
            }

            // ═══════════════════════════════════════════════════════
            //  PASSO 2 — Validação de Estado
            //  O sensor tem de estar no estado "ativo"
            // ═══════════════════════════════════════════════════════

            if (!string.Equals(sensor.Estado, "ativo", StringComparison.OrdinalIgnoreCase))
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_SENSOR_INACTIVE",
                    LogMessage = $"[VALIDAÇÃO] Sensor '{sensorId}' não está ativo (estado actual: '{sensor.Estado}')."
                };
            }

            // ═══════════════════════════════════════════════════════
            //  Extração antecipada do tipo para PASSO 3
            //  Split seguro: precisamos de pelo menos 2 blocos para extrair o tipo
            // ═══════════════════════════════════════════════════════

            string[] parts = rawMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // ═══════════════════════════════════════════════════════
            //  PASSO 3 — Validação de Tipo de Dado
            //  O tipo recebido tem de pertencer à lista autorizada DESTE sensor no CSV.
            //  Fazemos este passo antes da validação de formato completo (5 blocos),
            //  respeitando a ordem estrita do protocolo: Registo→Estado→Tipo→Conteúdo.
            // ═══════════════════════════════════════════════════════

            string tipoDado = parts.Length >= 2 ? parts[1] : "";

            bool tipoAutorizado = false;
            foreach (string t in sensor.TiposDados)
            {
                if (string.Equals(t, tipoDado, StringComparison.OrdinalIgnoreCase))
                {
                    tipoAutorizado = true;
                    break;
                }
            }

            if (!tipoAutorizado)
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_TYPE_NOT_SUPPORTED",
                    LogMessage = $"[VALIDAÇÃO] Tipo '{tipoDado}' não autorizado para o sensor '{sensorId}'. " +
                                 $"Tipos permitidos: [{string.Join(",", sensor.TiposDados)}]."
                };
            }

            // ═══════════════════════════════════════════════════════
            //  PASSO 4a — Validação de Formato
            //  A mensagem deve ter exatamente 5 blocos: DATA <tipo> <valor> <zona> <timestamp>
            // ═══════════════════════════════════════════════════════

            if (parts.Length != 5)
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_INVALID_DATA",
                    LogMessage = $"[VALIDAÇÃO] Mensagem com formato inválido — esperados 5 blocos, recebidos {parts.Length}."
                };
            }

            // Extrair os restantes parâmetros da mensagem
            string valorStr  = parts[2];  // ex: 22.5
            string zona      = parts[3];  // ex: ZONA_CENTRO
            string tsStr     = parts[4];  // ex: 2026-03-10T09:15:00

            // ═══════════════════════════════════════════════════════
            //  PASSO 4b — Validação de Tipo Definido Global
            //  O tipo tem de ser um dos tipos reconhecidos pelo sistema
            // ═══════════════════════════════════════════════════════

            if (!TiposGlobais.Contains(tipoDado))
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_INVALID_DATA",
                    LogMessage = $"[VALIDAÇÃO] Tipo '{tipoDado}' não é um tipo de dado reconhecido pelo sistema."
                };
            }

            // ═══════════════════════════════════════════════════════
            //  PASSO 4c — Validação de Zona
            //  Tem de ser uma das zonas definidas como válidas
            // ═══════════════════════════════════════════════════════

            if (!ZonasValidas.Contains(zona))
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_INVALID_DATA",
                    LogMessage = $"[VALIDAÇÃO] Zona '{zona}' inválida. Zonas permitidas: {string.Join(", ", ZonasValidas)}."
                };
            }

            // ═══════════════════════════════════════════════════════
            //  PASSO 4d — Validação de Valor
            //  Tem de ser numérico (int ou float).
            //  Para todos os tipos EXCETO "TEMP", o valor tem de ser >= 0.
            // ═══════════════════════════════════════════════════════

            double valorNumerico;
            if (!double.TryParse(valorStr, NumberStyles.Float | NumberStyles.AllowLeadingSign,
                                 CultureInfo.InvariantCulture, out valorNumerico))
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_INVALID_DATA",
                    LogMessage = $"[VALIDAÇÃO] Valor '{valorStr}' não é um número válido."
                };
            }

            // Regra especial: apenas TEMP aceita valores negativos
            bool isTEMP = string.Equals(tipoDado, "TEMP", StringComparison.OrdinalIgnoreCase);
            if (!isTEMP && valorNumerico < 0)
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_INVALID_DATA",
                    LogMessage = $"[VALIDAÇÃO] Valor negativo ({valorNumerico}) não permitido para o tipo '{tipoDado}' (apenas TEMP aceita negativos)."
                };
            }

            // ═══════════════════════════════════════════════════════
            //  PASSO 4e — Validação de Timestamp
            //  - Tem de ser uma data válida em formato ISO 8601
            //  - Não pode estar mais de 60 segundos no futuro
            // ═══════════════════════════════════════════════════════

            DateTime tsDateTime;
            if (!DateTime.TryParse(tsStr, CultureInfo.InvariantCulture,
                                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                    out tsDateTime))
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_INVALID_DATA",
                    LogMessage = $"[VALIDAÇÃO] Timestamp '{tsStr}' não é uma data válida em formato ISO 8601."
                };
            }

            // Verificar se o timestamp não está demasiado no futuro
            DateTime agora = DateTime.UtcNow;
            double diffSeconds = (tsDateTime - agora).TotalSeconds;

            if (diffSeconds > MAX_FUTURE_SECONDS)
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_INVALID_DATA",
                    LogMessage = $"[VALIDAÇÃO] Timestamp '{tsStr}' está {diffSeconds:F0}s no futuro (máximo permitido: {MAX_FUTURE_SECONDS}s)."
                };
            }

            // ═══════════════════════════════════════════════════════
            //  PASSO 5 — Sucesso: atualizar last_sync + preparar FORWARD
            // ═══════════════════════════════════════════════════════

            // Atualizar o last_sync do sensor para o momento atual (thread-safe via configManager)
            configManager.UpdateLastSync(sensorId, DateTime.UtcNow);

            // Construir a mensagem FORWARD para o servidor
            // Formato: FORWARD <sensor_id> <tipo> <valor> <zona> <timestamp>
            string forwardMsg = $"FORWARD {sensorId} {tipoDado} {valorStr} {zona} {tsStr}";

            return new DataValidationResult
            {
                IsValid = true,
                ErrorCode = null,
                ForwardMessage = forwardMsg,
                LogMessage = $"[VALIDAÇÃO] Mensagem DATA de '{sensorId}' validada com sucesso — tipo={tipoDado}, valor={valorStr}, zona={zona}."
            };
        }
    }
}
