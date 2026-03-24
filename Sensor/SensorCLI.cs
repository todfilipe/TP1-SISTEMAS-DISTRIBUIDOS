namespace Sensor;

/// <summary>
/// Interface de texto simples para o utilizador interagir com o Sensor.
/// Permite simular o envio de dados ambientais ao Gateway.
/// </summary>
public class SensorCLI
{
    private SensorClient? _sensor;

    // Tipos e zonas válidos segundo o protocolo
    private static readonly string[] TiposValidos =
        { "TEMP", "HUM", "AR", "RUIDO", "PM2.5", "PM10", "LUZ", "VIDEO" };

    private static readonly string[] ZonasValidas =
        { "ZONA_CENTRO", "ZONA_ESCOLAR", "ZONA_INDUSTRIAL", "ZONA_RESIDENCIAL", "ZONA_PARQUE" };

    public void Run(string gatewayHost, int gatewayPort = 8080)
    {
        Console.WriteLine("=== SENSOR — Monitorização Urbana One Health ===");
        Console.WriteLine();

        // Pedir sensor_id
        string sensorId = PedirInput("Introduza o sensor_id (ex: S101)");

        // Pedir tipos de dados
        Console.WriteLine();
        Console.WriteLine("Tipos de dados disponíveis:");
        for (int i = 0; i < TiposValidos.Length; i++)
            Console.WriteLine($"  {i + 1}. {TiposValidos[i]}");

        string tiposInput = PedirInput("Escolha os tipos (números separados por vírgula, ex: 1,2,4)");
        List<string> tiposSelecionados = ParseTipos(tiposInput);

        if (tiposSelecionados.Count == 0)
        {
            Console.WriteLine("[ERRO] Nenhum tipo válido selecionado. A sair.");
            return;
        }

        Console.WriteLine($"Tipos selecionados: {string.Join(", ", tiposSelecionados)}");
        Console.WriteLine();

        // Criar sensor e ligar ao Gateway
        _sensor = new SensorClient(sensorId, gatewayHost, gatewayPort);

        try
        {
            // 1. Ligação TCP
            Console.WriteLine($"A ligar ao Gateway em {gatewayHost}:{gatewayPort}...");
            _sensor.ConnectTcp();
            Console.WriteLine("[OK] Ligação TCP estabelecida.");

            // 2. CONNECT
            Console.WriteLine($"A enviar CONNECT {sensorId}...");
            string resp = _sensor.SendConnect();
            Console.WriteLine($"  Resposta: {resp}");

            if (!_sensor.IsConnected)
            {
                Console.WriteLine("[ERRO] Falha no CONNECT. A sair.");
                return;
            }

            // 3. REGISTER_TYPES
            Console.WriteLine($"A enviar REGISTER_TYPES {string.Join(",", tiposSelecionados)}...");
            resp = _sensor.SendRegisterTypes(tiposSelecionados);
            Console.WriteLine($"  Resposta: {resp}");

            if (!_sensor.IsOperational)
            {
                Console.WriteLine("[ERRO] Falha no REGISTER_TYPES. A sair.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("[OK] Sensor operacional. Pode enviar dados.");
            Console.WriteLine();

            // 4. Loop de comandos
            MostrarAjuda(tiposSelecionados);
            LoopComandos(tiposSelecionados);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERRO] {ex.Message}");
        }
        finally
        {
            _sensor.Dispose();
            Console.WriteLine("Sensor terminado.");
        }
    }

    private void LoopComandos(List<string> tiposRegistados)
    {
        while (true)
        {
            Console.WriteLine();
            string cmd = PedirInput("Comando").Trim().ToUpper();

            try
            {
                switch (cmd)
                {
                    case "DATA":
                        CmdData(tiposRegistados);
                        break;

                    case "HEARTBEAT":
                        CmdHeartbeat();
                        break;

                    case "DISCONNECT":
                        CmdDisconnect();
                        return;

                    case "AJUDA":
                    case "HELP":
                        MostrarAjuda(tiposRegistados);
                        break;

                    case "SAIR":
                    case "EXIT":
                        // Desconectar antes de sair
                        CmdDisconnect();
                        return;

                    default:
                        Console.WriteLine("Comando desconhecido. Escreva AJUDA para ver os comandos.");
                        break;
                }
            }
            catch (IOException)
            {
                Console.WriteLine("[ERRO] Ligação com o Gateway perdida.");
                return;
            }
            catch (TimeoutException)
            {
                Console.WriteLine("[ERRO] Timeout na comunicação com o Gateway.");
                return;
            }
        }
    }

    private void CmdData(List<string> tiposRegistados)
    {
        // Escolher tipo
        Console.WriteLine("Tipos registados:");
        for (int i = 0; i < tiposRegistados.Count; i++)
            Console.WriteLine($"  {i + 1}. {tiposRegistados[i]}");

        string tipoInput = PedirInput("Tipo (número ou nome)");
        string tipo = ResolveTipo(tipoInput, tiposRegistados);

        if (string.IsNullOrEmpty(tipo))
        {
            Console.WriteLine("[ERRO] Tipo inválido.");
            return;
        }

        // Valor
        string valor = PedirInput($"Valor para {tipo}");
        if (!double.TryParse(valor, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            Console.WriteLine("[ERRO] Valor deve ser numérico (ex: 22.5).");
            return;
        }

        // Zona
        Console.WriteLine("Zonas disponíveis:");
        for (int i = 0; i < ZonasValidas.Length; i++)
            Console.WriteLine($"  {i + 1}. {ZonasValidas[i]}");

        string zonaInput = PedirInput("Zona (número ou nome)");
        string zona = ResolveZona(zonaInput);

        if (string.IsNullOrEmpty(zona))
        {
            Console.WriteLine("[ERRO] Zona inválida.");
            return;
        }

        // Timestamp automático em UTC (consistente com o protocolo ISO 8601)
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

        Console.WriteLine($"A enviar DATA {tipo} {valor} {zona} {timestamp}...");
        string resp = _sensor!.SendData(tipo, valor, zona, timestamp);
        Console.WriteLine($"  Resposta: {resp}");
    }

    private void CmdHeartbeat()
    {
        Console.WriteLine($"A enviar HEARTBEAT {_sensor!.SensorId}...");
        string resp = _sensor.SendHeartbeat();
        Console.WriteLine($"  Resposta: {resp}");
    }

    private void CmdDisconnect()
    {
        Console.WriteLine($"A enviar DISCONNECT {_sensor!.SensorId}...");
        string resp = _sensor.SendDisconnect();
        Console.WriteLine($"  Resposta: {resp}");
    }

    private static void MostrarAjuda(List<string> tiposRegistados)
    {
        Console.WriteLine("--- Comandos disponíveis ---");
        Console.WriteLine("  DATA        — Enviar uma medição ambiental");
        Console.WriteLine("  HEARTBEAT   — Enviar sinal de vida");
        Console.WriteLine("  DISCONNECT  — Desconectar do Gateway");
        Console.WriteLine("  AJUDA       — Mostrar esta ajuda");
        Console.WriteLine("  SAIR        — Desconectar e terminar");
        Console.WriteLine($"  Tipos registados: {string.Join(", ", tiposRegistados)}");
        Console.WriteLine("----------------------------");
    }

    private List<string> ParseTipos(string input)
    {
        var result = new List<string>();
        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (int.TryParse(part, out int idx) && idx >= 1 && idx <= TiposValidos.Length)
                result.Add(TiposValidos[idx - 1]);
            else if (TiposValidos.Contains(part.ToUpper()))
                result.Add(part.ToUpper());
        }

        return result;
    }

    private static string ResolveTipo(string input, List<string> tiposRegistados)
    {
        if (int.TryParse(input, out int idx) && idx >= 1 && idx <= tiposRegistados.Count)
            return tiposRegistados[idx - 1];

        string upper = input.ToUpper();
        if (tiposRegistados.Contains(upper))
            return upper;

        return string.Empty;
    }

    private static string ResolveZona(string input)
    {
        if (int.TryParse(input, out int idx) && idx >= 1 && idx <= ZonasValidas.Length)
            return ZonasValidas[idx - 1];

        string upper = input.ToUpper();
        if (ZonasValidas.Contains(upper))
            return upper;

        return string.Empty;
    }

    private static string PedirInput(string prompt)
    {
        Console.Write($"{prompt}: ");
        return Console.ReadLine()?.Trim() ?? string.Empty;
    }
}
