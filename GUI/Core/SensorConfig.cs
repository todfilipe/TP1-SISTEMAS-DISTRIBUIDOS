using System;
using System.Collections.Generic;

namespace OneHealthMonitor.Core
{
    /// <summary>
    /// Representa a configuração de um sensor, carregada a partir do ficheiro sensors.csv.
    /// Formato CSV: sensor_id:estado:zona:[tipos_dados]:last_sync
    /// Exemplo:     S101:ativo:ZONA_CENTRO:[TEMP,HUM,RUIDO]:2026-03-10T08:45:00
    /// </summary>
    public class SensorConfig
    {
        /// <summary>Identificador único do sensor (ex: S101).</summary>
        public string SensorId { get; set; } = "";

        /// <summary>
        /// Estado atual do sensor.
        /// Valores possíveis: ativo, manutencao, desativado, indisponivel, desligado.
        /// </summary>
        public string Estado { get; set; } = "";

        /// <summary>Zona geográfica do sensor (ex: ZONA_CENTRO).</summary>
        public string Zona { get; set; } = "";

        /// <summary>Lista de tipos de dados que o sensor envia (ex: TEMP, HUM, RUIDO).</summary>
        public List<string> TiposDados { get; set; } = new();

        /// <summary>Data/hora da última sincronização (formato ISO 8601).</summary>
        public DateTime LastSync { get; set; }

        /// <summary>
        /// Serializa o objeto de volta para o formato CSV usado no ficheiro.
        /// </summary>
        public string ToCsvLine()
        {
            string tipos = "[" + string.Join(",", TiposDados) + "]";
            string syncStr = LastSync.ToString("yyyy-MM-ddTHH:mm:ss");
            return $"{SensorId}:{Estado}:{Zona}:{tipos}:{syncStr}";
        }

        public SensorConfig Clone()
        {
            return new SensorConfig
            {
                SensorId  = this.SensorId,
                Estado    = this.Estado,
                Zona      = this.Zona,
                TiposDados = new List<string>(this.TiposDados),
                LastSync  = this.LastSync
            };
        }

        public override string ToString()
        {
            return $"Sensor {SensorId} | Estado: {Estado} | Zona: {Zona} | Tipos: [{string.Join(",", TiposDados)}] | LastSync: {LastSync:yyyy-MM-ddTHH:mm:ss}";
        }
    }
}
