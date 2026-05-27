using Analysis;
using Servidor.Mongo.Models;
using Spectre.Console;
using System.Globalization;

namespace Servidor;

internal class CliHandler
{
    private readonly ServidorTCP servidor;

    public CliHandler(ServidorTCP servidor)
    {
        this.servidor = servidor;
    }

    public void ShowBanner()
    {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddRow(new FigletText("UrbanoDB").Color(Color.Cyan1));
        grid.AddRow(new Markup($"[bold white]TP2 Sistemas Distribuidos[/]  [grey]|[/]  TCP [green]{servidor.Porta}[/]  [grey]|[/]  MongoDB [yellow]urbanodb[/]"));
        grid.AddRow(new Markup("[grey]Escreve[/] [bold cyan]ajuda[/] [grey]para ver os comandos disponiveis.[/]"));

        AnsiConsole.Write(new Panel(grid)
            .Header("[bold green]Servidor One Health[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1));
    }

    public void Run()
    {
        while (servidor.IsRunning)
        {
            string? input = AnsiConsole.Ask<string>("[bold cyan]servidor>[/]");
            if (input == null)
            {
                continue;
            }

            string trimmed = input.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            try
            {
                ProcessCommand(trimmed);
            }
            catch (Exception ex)
            {
                ShowError($"Falha ao executar comando: {ex.Message}");
            }
        }
    }

    private void ProcessCommand(string input)
    {
        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string command = parts[0].ToLowerInvariant();
        string[] args = parts.Skip(1).ToArray();

        switch (command)
        {
            case "sair":
                AnsiConsole.MarkupLine("[yellow]A encerrar servidor...[/]");
                servidor.Stop();
                break;
            case "analisar":
                HandleAnalyze(args);
                break;
            case "prever":
                HandlePredict(args);
                break;
            case "historico":
                ShowHistory();
                break;
            case "leituras":
                ShowReadings(args).GetAwaiter().GetResult();
                break;
            case "analises":
                ShowPersistedAnalyses(args).GetAwaiter().GetResult();
                break;
            case "sensor":
                ShowSensor(args).GetAwaiter().GetResult();
                break;
            case "ajuda":
                ShowHelp();
                break;
            default:
                ShowError($"Comando desconhecido: {command}. Escreve ajuda para ver a lista.");
                break;
        }
    }

    private void HandleAnalyze(string[] args)
    {
        string type;
        string zone;
        string sensorId;
        string dateFrom;
        string dateTo;

        if (args.Length >= 5)
        {
            type = args[0];
            zone = args[1];
            sensorId = args[2];
            dateFrom = args[3];
            dateTo = args[4];
        }
        else
        {
            AnsiConsole.Write(new Rule("[yellow]Pedido de analise[/]").RuleStyle("grey"));
            type = AskRequired("Tipo de sensor", "TEMP");
            zone = AskRequired("Zona", "ZONA_CENTRO");
            sensorId = AskOptional("ID do sensor (opcional)");
            dateFrom = AskOptional("Data de inicio ISO 8601 (opcional)");
            dateTo = AskOptional("Data de fim ISO 8601 (opcional)");
        }

        var result = servidor.ExecutarAnalise(type, zone, sensorId, dateFrom, dateTo);
        if (result == null)
        {
            ShowError("Nao foi possivel executar a analise.");
            return;
        }

        ShowAnalysisResult(result);
    }

    private void HandlePredict(string[] args)
    {
        string type;
        string zone;
        int periods;
        string strategy;

        if (args.Length >= 3 && int.TryParse(args[2], out periods))
        {
            type = args[0];
            zone = args[1];
            strategy = args.Length >= 4 ? ServidorTCP.NormalizarEstrategia(args[3]) : "linear";
        }
        else
        {
            AnsiConsole.Write(new Rule("[yellow]Pedido de previsao[/]").RuleStyle("grey"));
            type = AskRequired("Tipo de sensor", "TEMP");
            zone = AskRequired("Zona", "ZONA_CENTRO");
            periods = AnsiConsole.Prompt(new TextPrompt<int>("Periodos a prever:").ValidationErrorMessage("[red]Introduz um inteiro valido.[/]"));
            strategy = ServidorTCP.NormalizarEstrategia(AskOptional("Estrategia (linear | ewma) [linear]"));
        }

        var result = servidor.ExecutarPrevisao(type, zone, periods, strategy);
        if (result == null)
        {
            ShowError("Nao foi possivel executar a previsao.");
            return;
        }

        ShowPredictionResult(result);
    }

    private async Task ShowReadings(string[] args)
    {
        if (servidor.ReadingsRepository == null)
        {
            ShowUnavailable("Repositório de leituras MongoDB indisponivel.");
            return;
        }

        var options = ParseOptions(args, new[] { "sensor", "zona", "tipo", "from", "to", "limit" });
        DateTime? from = ParseOptionalDate(options.GetValueOrDefault("from"));
        DateTime? to = ParseOptionalDate(options.GetValueOrDefault("to"));
        int? limit = ParseOptionalInt(options.GetValueOrDefault("limit")) ?? 50;

        var readings = await servidor.ReadingsRepository.FindAsync(
            sensorId: NormalizeEmpty(options.GetValueOrDefault("sensor")),
            zone: NormalizeEmpty(options.GetValueOrDefault("zona")),
            type: NormalizeEmpty(options.GetValueOrDefault("tipo")),
            from: from,
            to: to,
            limit: limit);

        if (readings.Count == 0)
        {
            ShowUnavailable("Nao foram encontradas leituras para os filtros indicados.");
            return;
        }

        var table = new Table()
            .Title("[bold cyan]Leituras MongoDB[/]")
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1);

        table.AddColumn("[bold]Sensor[/]");
        table.AddColumn("[bold]Zona[/]");
        table.AddColumn("[bold]Tipo[/]");
        table.AddColumn("[bold]Valor[/]");
        table.AddColumn("[bold]Unidade[/]");
        table.AddColumn("[bold]Timestamp[/]");
        table.AddColumn("[bold]Gateway[/]");

        foreach (var reading in readings)
        {
            table.AddRow(
                Escape(reading.SensorId),
                Escape(reading.Zone),
                $"[cyan]{Escape(reading.Type)}[/]",
                reading.Value.ToString("0.###", CultureInfo.InvariantCulture),
                Escape(reading.Unit),
                FormatDate(reading.Timestamp),
                Escape(reading.GatewayId));
        }

        AnsiConsole.Write(table);
    }

    private async Task ShowPersistedAnalyses(string[] args)
    {
        if (servidor.AnalysesRepository == null)
        {
            ShowUnavailable("Repositório de analises MongoDB indisponivel.");
            return;
        }

        var options = ParseOptions(args, new[] { "tipo", "zona", "limit" });
        int? limit = ParseOptionalInt(options.GetValueOrDefault("limit")) ?? 50;
        var analyses = await servidor.AnalysesRepository.FindAsync(
            type: NormalizeEmpty(options.GetValueOrDefault("tipo")),
            zone: NormalizeEmpty(options.GetValueOrDefault("zona")),
            limit: limit);

        if (analyses.Count == 0)
        {
            ShowUnavailable("Nao foram encontradas analises persistidas para os filtros indicados.");
            return;
        }

        var table = new Table()
            .Title("[bold cyan]Analises Persistidas[/]")
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1);

        table.AddColumn("[bold]Zona[/]");
        table.AddColumn("[bold]Tipo[/]");
        table.AddColumn("[bold]Janela[/]");
        table.AddColumn("[bold]Media[/]");
        table.AddColumn("[bold]Desvio[/]");
        table.AddColumn("[bold]P25[/]");
        table.AddColumn("[bold]P75[/]");
        table.AddColumn("[bold]P95[/]");
        table.AddColumn("[bold]Tendencia[/]");
        table.AddColumn("[bold]Alerta[/]");

        foreach (var analysis in analyses)
        {
            table.AddRow(
                Escape(analysis.Zone),
                $"[cyan]{Escape(analysis.Type)}[/]",
                $"{FormatDate(analysis.WindowStart)} -> {FormatDate(analysis.WindowEnd)}",
                analysis.Average.ToString("0.###", CultureInfo.InvariantCulture),
                analysis.StandardDeviation.ToString("0.###", CultureInfo.InvariantCulture),
                analysis.Percentile25.ToString("0.###", CultureInfo.InvariantCulture),
                analysis.Percentile75.ToString("0.###", CultureInfo.InvariantCulture),
                analysis.Percentile95.ToString("0.###", CultureInfo.InvariantCulture),
                ColorizeTrend(analysis.TrendClassification),
                ColorizeAlertLevel(analysis.AlertLevel));
        }

        AnsiConsole.Write(table);
    }

    private async Task ShowSensor(string[] args)
    {
        if (servidor.SensorsMetadataRepository == null || servidor.ReadingsRepository == null)
        {
            ShowUnavailable("Repositorios MongoDB indisponiveis.");
            return;
        }

        string sensorId = args.Length >= 1 ? args[0] : AskRequired("ID do sensor", "SENSOR_001");
        var metadata = await servidor.SensorsMetadataRepository.GetBySensorIdAsync(sensorId);
        if (metadata == null)
        {
            ShowUnavailable($"Nao existem metadados para o sensor {sensorId}.");
            return;
        }

        var metadataTable = new Grid();
        metadataTable.AddColumn();
        metadataTable.AddColumn();
        metadataTable.AddRow("[bold]Sensor[/]", Escape(metadata.SensorId));
        metadataTable.AddRow("[bold]Zona[/]", Escape(metadata.Zone));
        metadataTable.AddRow("[bold]Tipo[/]", Escape(metadata.Type));
        metadataTable.AddRow("[bold]Primeira leitura[/]", FormatDate(metadata.FirstReadingAt));
        metadataTable.AddRow("[bold]Ultima leitura[/]", FormatDate(metadata.LastReadingAt));
        metadataTable.AddRow("[bold]Total[/]", metadata.TotalReadings.ToString(CultureInfo.InvariantCulture));

        AnsiConsole.Write(new Panel(metadataTable)
            .Header("[bold cyan]Metadados do Sensor[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1));

        var readings = await servidor.ReadingsRepository.FindAsync(sensorId: sensorId, limit: 10);
        if (readings.Count == 0)
        {
            ShowUnavailable("Nao existem leituras recentes para este sensor.");
            return;
        }

        var table = new Table()
            .Title("[bold cyan]Ultimas 10 Leituras[/]")
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1);

        table.AddColumn("[bold]Zona[/]");
        table.AddColumn("[bold]Tipo[/]");
        table.AddColumn("[bold]Valor[/]");
        table.AddColumn("[bold]Unidade[/]");
        table.AddColumn("[bold]Timestamp[/]");
        table.AddColumn("[bold]Gateway[/]");

        foreach (var reading in readings)
        {
            table.AddRow(
                Escape(reading.Zone),
                $"[cyan]{Escape(reading.Type)}[/]",
                reading.Value.ToString("0.###", CultureInfo.InvariantCulture),
                Escape(reading.Unit),
                FormatDate(reading.Timestamp),
                Escape(reading.GatewayId));
        }

        AnsiConsole.Write(table);
    }

    private void ShowAnalysisResult(AnalysisResult result)
    {
        bool failed = IsFailure(result.ResultSummary);
        var table = new Table()
            .Border(TableBorder.Simple)
            .HideHeaders();

        table.AddColumn("Metrica");
        table.AddColumn("Valor");
        table.AddRow("[bold]Resumo[/]", Escape(result.ResultSummary));
        table.AddRow("[bold]Media[/]", result.ComputedAverage.ToString("0.###", CultureInfo.InvariantCulture));
        table.AddRow("[bold]Mean[/]", result.Mean.ToString("0.###", CultureInfo.InvariantCulture));
        table.AddRow("[bold]Mediana[/]", result.Median.ToString("0.###", CultureInfo.InvariantCulture));
        table.AddRow("[bold]Percentil 25[/]", result.Percentile25.ToString("0.###", CultureInfo.InvariantCulture));
        table.AddRow("[bold]Percentil 75[/]", result.Percentile75.ToString("0.###", CultureInfo.InvariantCulture));
        table.AddRow("[bold]Percentil 95[/]", result.Percentile95.ToString("0.###", CultureInfo.InvariantCulture));
        table.AddRow("[bold]StdDev[/]", result.StdDev.ToString("0.###", CultureInfo.InvariantCulture));
        table.AddRow("[bold]Min / Max[/]", $"{result.Min:0.###} / {result.Max:0.###}");
        table.AddRow("[bold]Outliers[/]", result.OutliersCount.ToString(CultureInfo.InvariantCulture));
        table.AddRow("[bold]Tendencia[/]", ColorizeTrend(result.Trend));
        table.AddRow("[bold]Amostras[/]", result.SampleCount.ToString(CultureInfo.InvariantCulture));
        table.AddRow("[bold]Timestamp[/]", Escape(result.Timestamp));

        AnsiConsole.Write(new Panel(table)
            .Header(failed ? "[bold red]Falha da Analise[/]" : "[bold green]Resultado da Analise[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(failed ? Color.Red : Color.Green));
    }

    private void ShowPredictionResult(PredictionResult result)
    {
        bool failed = IsFailure(result.PredictionSummary);
        var table = new Table()
            .Border(TableBorder.Simple)
            .HideHeaders();

        table.AddColumn("Campo");
        table.AddColumn("Valor");
        table.AddRow("[bold]Resumo[/]", Escape(result.PredictionSummary));
        table.AddRow("[bold]Estrategia[/]", Escape(result.StrategyUsed));
        table.AddRow("[bold]Forecast[/]", Escape(string.Join(", ", result.Forecast.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)))));
        table.AddRow("[bold]Timestamp[/]", Escape(result.Timestamp));

        AnsiConsole.Write(new Panel(table)
            .Header(failed ? "[bold red]Falha da Previsao[/]" : "[bold green]Resultado da Previsao[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(failed ? Color.Red : Color.Green));
    }

    private void ShowHistory()
    {
        var (analyses, predictions) = servidor.ObterHistorico();

        var analysisTable = new Table()
            .Title($"[bold cyan]Historico de Analises ({analyses.Count})[/]")
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1);
        analysisTable.AddColumn("#");
        analysisTable.AddColumn("Timestamp");
        analysisTable.AddColumn("Media");
        analysisTable.AddColumn("Alerta");
        analysisTable.AddColumn("Resumo");

        for (int i = 0; i < analyses.Count; i++)
        {
            var result = analyses[i];
            analysisTable.AddRow(
                (i + 1).ToString(CultureInfo.InvariantCulture),
                Escape(result.Timestamp),
                result.ComputedAverage.ToString("0.###", CultureInfo.InvariantCulture),
                Escape(result.AlertLevel),
                Escape(result.ResultSummary));
        }

        var predictionTable = new Table()
            .Title($"[bold cyan]Historico de Previsoes ({predictions.Count})[/]")
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1);
        predictionTable.AddColumn("#");
        predictionTable.AddColumn("Timestamp");
        predictionTable.AddColumn("Estrategia");
        predictionTable.AddColumn("Forecast");
        predictionTable.AddColumn("Resumo");

        for (int i = 0; i < predictions.Count; i++)
        {
            var result = predictions[i];
            predictionTable.AddRow(
                (i + 1).ToString(CultureInfo.InvariantCulture),
                Escape(result.Timestamp),
                Escape(result.StrategyUsed),
                Escape(string.Join(", ", result.Forecast.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)))),
                Escape(result.PredictionSummary));
        }

        if (analyses.Count == 0 && predictions.Count == 0)
        {
            ShowUnavailable("Ainda nao existem analises ou previsoes em memoria.");
            return;
        }

        AnsiConsole.Write(analysisTable);
        AnsiConsole.Write(predictionTable);
    }

    private void ShowHelp()
    {
        var table = new Table()
            .Title("[bold cyan]Comandos Disponiveis[/]")
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1);

        table.AddColumn("[bold]Comando[/]");
        table.AddColumn("[bold]Sintaxe[/]");
        table.AddColumn("[bold]Descricao[/]");

        table.AddRow("[green]analisar[/]", "[grey]analisar <tipo> <zona> <sensor|- > <from|- > <to|- >[/]", "Executa analise gRPC e persiste resultado.");
        table.AddRow("[green]prever[/]", "[grey]prever <tipo> <zona> <periodos> [linear|ewma][/] ", "Executa previsao gRPC.");
        table.AddRow("[green]historico[/]", "[grey]historico[/]", "Mostra analises e previsoes em memoria.");
        table.AddRow("[green]leituras[/]", "[grey]leituras sensor=ID zona=Z tipo=TEMP from=ISO to=ISO limit=50[/]", "Lista leituras persistidas no MongoDB.");
        table.AddRow("[green]analises[/]", "[grey]analises tipo=TEMP zona=ZONA_CENTRO limit=50[/]", "Lista analises persistidas no MongoDB.");
        table.AddRow("[green]sensor[/]", "[grey]sensor <sensorId>[/]", "Mostra metadados e ultimas dez leituras do sensor.");
        table.AddRow("[green]ajuda[/]", "[grey]ajuda[/]", "Mostra esta tabela.");
        table.AddRow("[green]sair[/]", "[grey]sair[/]", "Encerra o servidor.");

        AnsiConsole.Write(table);
    }

    private static Dictionary<string, string> ParseOptions(string[] args, IReadOnlyList<string> positionalKeys)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int positional = 0;

        foreach (string arg in args)
        {
            int separator = arg.IndexOf('=');
            if (separator > 0)
            {
                string key = arg[..separator].Trim().ToLowerInvariant();
                string value = arg[(separator + 1)..].Trim();
                result[key] = value;
                continue;
            }

            if (positional < positionalKeys.Count)
            {
                result[positionalKeys[positional]] = arg;
                positional++;
            }
        }

        return result;
    }

    private static string AskRequired(string label, string example)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>($"{label} [grey](ex: {example})[/]:")
                .Validate(value => string.IsNullOrWhiteSpace(value)
                    ? ValidationResult.Error("[red]Valor obrigatorio.[/]")
                    : ValidationResult.Success()));
    }

    private static string AskOptional(string label)
    {
        return AnsiConsole.Prompt(new TextPrompt<string>($"{label}:").AllowEmpty());
    }

    private static string? NormalizeEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value == "-" ? null : value;
    }

    private static DateTime? ParseOptionalDate(string? value)
    {
        value = NormalizeEmpty(value);
        if (value == null)
        {
            return null;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed)
            ? parsed
            : null;
    }

    private static int? ParseOptionalInt(string? value)
    {
        value = NormalizeEmpty(value);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : null;
    }

    private static string FormatDate(DateTime date)
    {
        return date.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string ColorizeTrend(string trend)
    {
        string escaped = Escape(string.IsNullOrWhiteSpace(trend) ? "N/A" : trend);
        string normalized = trend.ToLowerInvariant();

        if (normalized.Contains("increasing") || normalized.Contains("cresc"))
        {
            return $"[green]{escaped}[/]";
        }

        if (normalized.Contains("stable") || normalized.Contains("estavel") || normalized.Contains("estável"))
        {
            return $"[yellow]{escaped}[/]";
        }

        if (normalized.Contains("decreasing") || normalized.Contains("decresc"))
        {
            return $"[red]{escaped}[/]";
        }

        return $"[grey]{escaped}[/]";
    }

    private static string ColorizeAlertLevel(string alertLevel)
    {
        string escaped = Escape(string.IsNullOrWhiteSpace(alertLevel) ? "N/A" : alertLevel);
        string normalized = alertLevel.ToUpperInvariant();

        return normalized switch
        {
            "NORMAL" => $"[green]{escaped}[/]",
            "WARNING" => $"[yellow]{escaped}[/]",
            "CRITICAL" => $"[red]{escaped}[/]",
            _ => $"[grey]{escaped}[/]"
        };
    }

    private static string Escape(string value)
    {
        return Markup.Escape(value ?? string.Empty);
    }

    private static bool IsFailure(string value)
    {
        return value?.TrimStart().StartsWith("FALHA:", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static void ShowUnavailable(string message)
    {
        AnsiConsole.Write(new Panel(Markup.Escape(message))
            .Header("[yellow]Sem resultados[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Yellow));
    }

    private static void ShowError(string message)
    {
        AnsiConsole.Write(new Panel(Markup.Escape(message))
            .Header("[red]Erro[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Red));
    }
}
