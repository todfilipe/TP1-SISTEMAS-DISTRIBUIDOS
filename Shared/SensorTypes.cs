namespace Shared;

public static class SensorTypes
{
    private static readonly IReadOnlyDictionary<string, string> UnitsByType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TEMP"] = "C",
            ["HUM"] = "%",
            ["AR"] = "ug/m3",
            ["RUIDO"] = "dB",
            ["PM2.5"] = "ug/m3",
            ["PM10"] = "ug/m3",
            ["LUZ"] = "lux",
            ["VIDEO"] = "frame"
        };

    public static string GetUnitForType(string? type, string fallback = "n/a")
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return fallback;
        }

        return UnitsByType.TryGetValue(type.Trim(), out string? unit)
            ? unit
            : fallback;
    }
}
