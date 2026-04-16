using System;
using System.Collections.Generic;

namespace OneHealthMonitor.Core
{
    public class LiveDataItem
    {
        public string SensorId { get; set; } = "";
        public string Tipo { get; set; } = "";
        public string Valor { get; set; } = "";
        public string Unidade { get; set; } = "";
        public string Zona { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Cor { get; set; } = "#00BFA5";

        private static readonly Dictionary<string, string> TipoCores = new(StringComparer.OrdinalIgnoreCase)
        {
            { "TEMP", "#FF8C00" },
            { "HUM", "#2196F3" },
            { "AR", "#4CAF50" },
            { "RUIDO", "#9C27B0" },
            { "PM2.5", "#EF5350" },
            { "PM10", "#F44336" },
            { "LUZ", "#FFC107" },
            { "VIDEO", "#00BCD4" }
        };

        private static readonly Dictionary<string, string> TipoUnidades = new(StringComparer.OrdinalIgnoreCase)
        {
            { "TEMP", "°C" },
            { "HUM", "%" },
            { "AR", "" },
            { "RUIDO", "dB" },
            { "PM2.5", "µg/m³" },
            { "PM10", "µg/m³" },
            { "LUZ", "lx" },
            { "VIDEO", "" }
        };

        public static LiveDataItem Create(string sensorId, string tipo, string valor, string zona, string timestamp)
        {
            return new LiveDataItem
            {
                SensorId = sensorId,
                Tipo = tipo,
                Valor = valor,
                Unidade = TipoUnidades.TryGetValue(tipo, out var u) ? u : "",
                Zona = zona,
                Timestamp = timestamp,
                Cor = TipoCores.TryGetValue(tipo, out var c) ? c : "#00BFA5"
            };
        }

        public static string GetColorForType(string tipo)
        {
            return TipoCores.TryGetValue(tipo, out var c) ? c : "#00BFA5";
        }

        public static string GetUnitForType(string tipo)
        {
            return TipoUnidades.TryGetValue(tipo, out var u) ? u : "";
        }
    }
}
