using System.Globalization;

namespace Servidor;

internal static class ProtocolHelpers
{
    // Aceita apenas "linear" ou "ewma"; qualquer outro valor (ou vazio) recai em "linear".
    internal static string NormalizarEstrategia(string? input)
    {
        string s = (input ?? "").Trim().ToLowerInvariant();
        return s == "ewma" ? "ewma" : "linear";
    }

    internal static string? NormalizarFiltroOpcional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value.Trim() == "-" ? null : value.Trim();
    }

    internal static bool TryParseFiltroTemporal(string? value, out DateTime? parsed, out string? erro)
    {
        parsed = null;
        erro = null;

        string? normalized = NormalizarFiltroOpcional(value);
        if (normalized == null)
        {
            return true;
        }

        if (TryParseUtc(normalized, out DateTime parsedDate))
        {
            parsed = parsedDate;
            return true;
        }

        erro = $"Filtro temporal invalido: '{value}'.";
        return false;
    }

    internal static string DescreverFiltros(string? tipo, string? zona, string? sensorId, DateTime? from, DateTime? to)
    {
        var filtros = new List<string>();

        if (!string.IsNullOrWhiteSpace(tipo)) filtros.Add($"tipo='{tipo}'");
        if (!string.IsNullOrWhiteSpace(zona)) filtros.Add($"zona='{zona}'");
        if (!string.IsNullOrWhiteSpace(sensorId)) filtros.Add($"sensorId='{sensorId}'");
        if (from.HasValue) filtros.Add($"from='{from.Value:yyyy-MM-ddTHH:mm:ssZ}'");
        if (to.HasValue) filtros.Add($"to='{to.Value:yyyy-MM-ddTHH:mm:ssZ}'");

        return filtros.Count == 0 ? "sem filtros" : string.Join(", ", filtros);
    }

    internal static DateTime ResolveWindowBoundary(string? candidate, IEnumerable<string> readingTimestamps, bool useMinimum)
    {
        if (!string.IsNullOrWhiteSpace(candidate) && TryParseUtc(candidate, out DateTime parsedCandidate))
        {
            return parsedCandidate;
        }

        var parsedReadings = readingTimestamps
            .Where(ts => TryParseUtc(ts, out _))
            .Select(ParseDateTimeOrUtcNow)
            .ToList();

        if (parsedReadings.Count == 0)
        {
            return DateTime.UtcNow;
        }

        return useMinimum ? parsedReadings.Min() : parsedReadings.Max();
    }

    internal static DateTime ParseDateTimeOrUtcNow(string value)
    {
        return TryParseUtc(value, out DateTime parsed) ? parsed : DateTime.UtcNow;
    }

    internal static bool TryParseUtc(string value, out DateTime parsed)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsed);
    }
}
