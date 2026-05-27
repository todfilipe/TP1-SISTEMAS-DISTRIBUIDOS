namespace Sensor;

// Contrato partilhado entre Sensor e Gateway (incluído como linked-file no Gateway.csproj).
// Os nomes das propriedades correspondem ao payload JSON trocado entre os dois.
public class SensorMessage
{
    public string sensorId { get; set; } = null!;
    public string zone { get; set; } = null!;
    public string type { get; set; } = null!;
    public double value { get; set; }
    public string unit { get; set; } = null!;
    public string timestamp { get; set; } = null!;
    public string raw { get; set; } = null!;
    public string rawFormat { get; set; } = null!;
}
