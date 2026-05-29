using System;
using System.Collections.Generic;

namespace Shared;

public static class ProtocolConstants
{
    public static readonly string[] SensorTypes =
    [
        "TEMP",
        "HUM",
        "AR",
        "RUIDO",
        "PM2.5",
        "PM10",
        "LUZ",
        "VIDEO"
    ];

    public static readonly string[] Zones =
    [
        "ZONA_CENTRO",
        "ZONA_ESCOLAR",
        "ZONA_INDUSTRIAL",
        "ZONA_RESIDENCIAL",
        "ZONA_PARQUE"
    ];

    public static readonly string[] SensorStates =
    [
        "ativo",
        "manutencao",
        "desativado",
        "indisponivel",
        "desligado"
    ];

    public static readonly IReadOnlySet<string> SensorTypeSet =
        new HashSet<string>(SensorTypes, StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlySet<string> ZoneSet =
        new HashSet<string>(Zones, StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> SensorStateSet =
        new HashSet<string>(SensorStates, StringComparer.Ordinal);

    public static bool IsValidSensorType(string? type)
    {
        return !string.IsNullOrWhiteSpace(type) && SensorTypeSet.Contains(type.Trim());
    }

    public static bool IsValidZone(string? zone)
    {
        return !string.IsNullOrWhiteSpace(zone) && ZoneSet.Contains(zone.Trim());
    }

    public static bool IsValidSensorState(string? state)
    {
        return !string.IsNullOrWhiteSpace(state) && SensorStateSet.Contains(state.Trim());
    }
}
