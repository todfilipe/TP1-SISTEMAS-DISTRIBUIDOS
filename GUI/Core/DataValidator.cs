using System;
using System.Collections.Generic;
using System.Globalization;

namespace OneHealthMonitor.Core
{
    /// <summary>
    /// Resultado da validação de uma mensagem DATA.
    /// </summary>
    public class DataValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorCode { get; set; }
        public string? ForwardMessage { get; set; }
        public string? LogMessage { get; set; }
    }

    /// <summary>
    /// Validador completo de mensagens DATA enviadas pelos sensores.
    /// </summary>
    public static class DataValidator
    {
        private static readonly HashSet<string> ZonasValidas = new HashSet<string>(StringComparer.Ordinal)
        {
            "ZONA_CENTRO", "ZONA_ESCOLAR", "ZONA_INDUSTRIAL", "ZONA_RESIDENCIAL", "ZONA_PARQUE"
        };

        private static readonly HashSet<string> TiposGlobais = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TEMP", "HUM", "AR", "RUIDO", "PM2.5", "PM10", "LUZ", "VIDEO"
        };

        private const int MAX_FUTURE_SECONDS = 60;

        public static DataValidationResult ValidateAndProcessData(
            string rawMessage,
            string sensorId,
            SensorConfigManager configManager,
            List<string>? sessionTypes = null)
        {
            SensorConfig? sensor = configManager.GetSensor(sensorId);

            if (sensor == null)
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_NOT_REGISTERED",
                    LogMessage = $"[VALIDAÇÃO] Sensor '{sensorId}' não está registado no CSV."
                };
            }

            string estadoActual = sensor.Estado.ToLower();
            if (estadoActual == "desativado" || estadoActual == "manutencao")
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_SENSOR_INACTIVE",
                    LogMessage = $"[VALIDAÇÃO] Sensor '{sensorId}' rejeitado. Estado: '{sensor.Estado}'."
                };
            }

            string[] parts = rawMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string tipoDado = parts.Length >= 2 ? parts[1] : "";

            bool tipoAutorizado = false;
            var listaAVerificar = (sessionTypes != null && sessionTypes.Count > 0)
                ? sessionTypes
                : sensor.TiposDados;

            foreach (string t in listaAVerificar)
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
                    LogMessage = $"[VALIDAÇÃO] Tipo '{tipoDado}' não registado pelo sensor '{sensorId}'."
                };
            }

            if (parts.Length != 5)
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_INVALID_DATA",
                    LogMessage = $"[VALIDAÇÃO] Formato inválido — esperados 5 blocos, recebidos {parts.Length}."
                };
            }

            string valorStr = parts[2];
            string zona = parts[3];
            string tsStr = parts[4];

            if (!TiposGlobais.Contains(tipoDado))
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_INVALID_DATA",
                    LogMessage = $"[VALIDAÇÃO] Tipo '{tipoDado}' não reconhecido."
                };
            }

            if (!string.Equals(zona, sensor.Zona, StringComparison.OrdinalIgnoreCase))
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_INVALID_DATA",
                    LogMessage = $"[VALIDAÇÃO] Zona '{zona}' não corresponde à zona do sensor '{sensor.Zona}'."
                };
            }

            double valorNumerico;
            if (!double.TryParse(valorStr, NumberStyles.Float | NumberStyles.AllowLeadingSign,
                                 CultureInfo.InvariantCulture, out valorNumerico))
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_INVALID_DATA",
                    LogMessage = $"[VALIDAÇÃO] Valor '{valorStr}' não é numérico."
                };
            }

            bool isTEMP = string.Equals(tipoDado, "TEMP", StringComparison.OrdinalIgnoreCase);
            if (!isTEMP && valorNumerico < 0)
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_INVALID_DATA",
                    LogMessage = $"[VALIDAÇÃO] Valor negativo não permitido para '{tipoDado}'."
                };
            }

            DateTime tsDateTime;
            if (!DateTime.TryParse(tsStr, CultureInfo.InvariantCulture,
                                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                    out tsDateTime))
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_INVALID_DATA",
                    LogMessage = $"[VALIDAÇÃO] Timestamp '{tsStr}' inválido."
                };
            }

            double diffSeconds = (tsDateTime - DateTime.UtcNow).TotalSeconds;
            if (diffSeconds > MAX_FUTURE_SECONDS)
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ErrorCode = "ERR_INVALID_DATA",
                    LogMessage = $"[VALIDAÇÃO] Timestamp '{tsStr}' está {diffSeconds:F0}s no futuro."
                };
            }

            configManager.UpdateLastSync(sensorId, DateTime.UtcNow);
            string forwardMsg = $"FORWARD {sensorId} {tipoDado} {valorStr} {zona} {tsStr}";

            return new DataValidationResult
            {
                IsValid = true,
                ErrorCode = null,
                ForwardMessage = forwardMsg,
                LogMessage = $"[VALIDAÇÃO] DATA de '{sensorId}' validada — tipo={tipoDado}, valor={valorStr}, zona={zona}."
            };
        }
    }
}
