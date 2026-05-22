using Grpc.Net.Client;
using Grpc.Core;
using Analysis;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Servidor
{
    /// <summary>
    /// Servidor TCP que aguarda ligações de Gateways na porta 9090.
    /// Processa mensagens do protocolo: GW_CONNECT, FORWARD, SENSOR_STATUS, GW_DISCONNECT.
    /// Responde com OK/ERR conforme o protocolo definido.
    /// Suporta múltiplas ligações concorrentes (uma thread por Gateway).
    /// </summary>
    public class ServidorTCP
    {
        private readonly int _porta;
        private readonly DataStore _dataStore;
        private TcpListener? _listener;
        private volatile bool _running;

        // Conjunto de gateways ligados (HashSet para lookups eficientes)
        private readonly HashSet<string> _gatewaysLigados = new();
        private readonly object _gwListLock = new();

        // Estados válidos para SENSOR_STATUS (estático para evitar recriação)
        private static readonly HashSet<string> EstadosValidos = new()
        {
            "ativo", "manutencao", "desativado", "indisponivel", "desligado"
        };

                // gRPC Analysis Service configuration.
        // URL lido do appsettings.json; a variável de ambiente ANALYSIS_SERVICE_URL
        // funciona como sobrescrita (override), tal como noutros componentes do projeto.
        private static string AnalysisServiceUrl = CarregarAnalysisServiceUrl();
        private static GrpcChannel? _analysisChannel;
        private static AnalysisService.AnalysisServiceClient? _analysisClient;

        private static readonly List<AnalysisResult> HistoricoAnalises = new();
        private static readonly List<PredictionResult> HistoricoPrevisoes = new();
        private static readonly object HistoricoLock = new();

        // Pipeline de resiliência Polly para chamadas gRPC ao serviço de Análise.
        // Retry exponencial: 3 tentativas com backoff de 1/2/4 segundos.
        // Filosofia idêntica à do Gateway (paridade entre componentes).
        private static readonly ResiliencePipeline _grpcPipeline =
            new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    ShouldHandle = new PredicateBuilder()
                        .Handle<RpcException>(ex => ex.StatusCode == StatusCode.Unavailable ||
                                                    ex.StatusCode == StatusCode.DeadlineExceeded ||
                                                    ex.StatusCode == StatusCode.Internal)
                        .Handle<Exception>(),
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = false,
                    Delay = TimeSpan.FromSeconds(1),
                    OnRetry = args =>
                    {
                        Console.WriteLine($"[Servidor][Polly] Tentativa {args.AttemptNumber + 1} falhou. " +
                                          $"Nova tentativa em {args.RetryDelay.TotalSeconds}s. Erro: {args.Outcome.Exception?.Message}");
                        return default;
                    }
                })
                .AddTimeout(TimeSpan.FromSeconds(10))
                .Build();

        public ServidorTCP(int porta = 9090)
        {
            _porta = porta;
            _dataStore = new DataStore();
        }

        /// <summary>
        /// Inicia o servidor TCP e aguarda ligações de Gateways.
        /// </summary>
        public void Iniciar()
        {
            InicializarClienteAnalise();
            _listener = new TcpListener(IPAddress.Any, _porta);
            _listener.Start();
            _running = true;

            Console.WriteLine("╔══════════════════════════════════════════════╗");
            Console.WriteLine("║   SERVIDOR — Monitorização Urbana One Health ║");
            Console.WriteLine("╠══════════════════════════════════════════════╣");
            Console.WriteLine($"║   A escutar na porta {_porta}...            ║");
            Console.WriteLine("╚══════════════════════════════════════════════╝");
            Console.WriteLine();

            // Thread para input do utilizador (comando de encerramento)
            Thread inputThread = new Thread(LerInput);
            inputThread.IsBackground = true;
            inputThread.Start();

            try
            {
                while (_running)
                {
                    // Verificar se há ligações pendentes (com timeout para permitir encerramento)
                    if (_listener.Pending())
                    {
                        TcpClient client = _listener.AcceptTcpClient();
                        string endpointRemoto = client.Client.RemoteEndPoint?.ToString() ?? "desconhecido";
                        Console.WriteLine($"[Servidor] Nova ligação TCP de: {endpointRemoto}");

                        // Criar thread para tratar este Gateway
                        Thread clientThread = new Thread(() => TratarGateway(client));
                        clientThread.IsBackground = true;
                        clientThread.Start();
                    }
                    else
                    {
                        Thread.Sleep(100); // Evitar busy-wait
                    }
                }
            }
            catch (SocketException) when (!_running)
            {
                // Servidor encerrado normalmente
                Console.WriteLine("[Servidor] Encerrado.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Servidor] ERRO: {ex.Message}");
            }
            finally
            {
                _listener?.Stop();
            }
        }

        /// <summary>
        /// Lê input do utilizador para comandos do servidor (ex: "sair").
        /// </summary>
        private void LerInput()
        {
            while (_running)
            {
                string? input = Console.ReadLine();
                if (input == null) continue;

                string trimmed = input.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string cmd = parts[0].ToLower();

                if (cmd == "sair")
                {
                    Console.WriteLine("[Servidor] A encerrar...");
                    _running = false;
                    _listener?.Stop();
                    break;
                }
                else if (cmd == "analisar")
                {
                    if (parts.Length >= 6)
                    {
                        string tipo = parts[1];
                        string zona = parts[2];
                        string sensorId = parts[3];
                        string dateFrom = parts[4];
                        string dateTo = parts[5];
                        ExecutarAnalise(tipo, zona, sensorId, dateFrom, dateTo);
                    }
                    else
                    {
                        Console.WriteLine("\n--- Pedido de Análise Interativo ---");
                        Console.Write("Introduza o Tipo de Sensor (ex: TEMP, HUM): ");
                        string? tipo = Console.ReadLine()?.Trim();
                        
                        Console.Write("Introduza a Zona (ex: ZONA_CENTRO): ");
                        string? zona = Console.ReadLine()?.Trim();

                        Console.Write("Introduza o ID do Sensor (opcional, Enter para todos): ");
                        string? sensorId = Console.ReadLine()?.Trim();

                        Console.Write("Introduza a Data de Início (formato ISO 8601, opcional): ");
                        string? dateFrom = Console.ReadLine()?.Trim();

                        Console.Write("Introduza a Data de Fim (formato ISO 8601, opcional): ");
                        string? dateTo = Console.ReadLine()?.Trim();

                        if (string.IsNullOrEmpty(tipo) || string.IsNullOrEmpty(zona))
                        {
                            Console.WriteLine("[AVISO] Tipo e Zona são obrigatórios para a análise.");
                        }
                        else
                        {
                            ExecutarAnalise(tipo, zona, sensorId ?? "", dateFrom ?? "", dateTo ?? "");
                        }
                    }
                }
                else if (cmd == "prever")
                {
                    if (parts.Length >= 4)
                    {
                        string tipo = parts[1];
                        string zona = parts[2];
                        if (int.TryParse(parts[3], out int periodos))
                        {
                            // Estratégia opcional como 5º argumento (linear|ewma).
                            string strategy = parts.Length >= 5 ? NormalizarEstrategia(parts[4]) : "linear";
                            ExecutarPrevisao(tipo, zona, periodos, strategy);
                        }
                        else
                        {
                            Console.WriteLine("[ERRO] Número de períodos inválido.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("\n--- Pedido de Previsão Interativo ---");
                        Console.Write("Introduza o Tipo de Sensor (ex: TEMP, HUM): ");
                        string? tipo = Console.ReadLine()?.Trim();

                        Console.Write("Introduza a Zona (ex: ZONA_CENTRO): ");
                        string? zona = Console.ReadLine()?.Trim();

                        Console.Write("Introduza o número de períodos a prever: ");
                        string? periodosStr = Console.ReadLine()?.Trim();

                        Console.Write("Introduza a estratégia de previsão (linear | ewma) [linear]: ");
                        string strategy = NormalizarEstrategia(Console.ReadLine()?.Trim());

                        if (string.IsNullOrEmpty(tipo) || string.IsNullOrEmpty(zona) || !int.TryParse(periodosStr, out int periodos))
                        {
                            Console.WriteLine("[AVISO] Parâmetros de previsão inválidos.");
                        }
                        else
                        {
                            ExecutarPrevisao(tipo, zona, periodos, strategy);
                        }
                    }
                }
                else if (cmd == "historico")
                {
                    lock (HistoricoLock)
                    {
                        Console.WriteLine($"\n--- Histórico de Análises Realizadas ({HistoricoAnalises.Count} registos) ---");
                        for (int i = 0; i < HistoricoAnalises.Count; i++)
                        {
                            var r = HistoricoAnalises[i];
                            Console.WriteLine($"[{i + 1}] Time: {r.Timestamp} | Avg: {r.ComputedAverage:F2} | Alert: {r.AlertLevel} | Summary: {r.ResultSummary}");
                        }
                        Console.WriteLine("-------------------------------------------------------------------------");

                        Console.WriteLine($"\n--- Histórico de Previsões Realizadas ({HistoricoPrevisoes.Count} registos) ---");
                        for (int i = 0; i < HistoricoPrevisoes.Count; i++)
                        {
                            var p = HistoricoPrevisoes[i];
                            Console.WriteLine($"[{i + 1}] Time: {p.Timestamp} | Estratégia: {p.StrategyUsed} | Forecast: [{string.Join(", ", p.Forecast)}] | Summary: {p.PredictionSummary}");
                        }
                        Console.WriteLine("-------------------------------------------------------------------------\n");
                    }
                }
                else if (cmd == "ajuda")
                {
                    Console.WriteLine("\n--- Comandos Disponíveis ---");
                    Console.WriteLine("  sair       - Encerra o Servidor.");
                    Console.WriteLine("  analisar   - Inicia análise interativa.");
                    Console.WriteLine("  prever     - Inicia previsão interativa.");
                    Console.WriteLine("  historico  - Mostra o histórico de análises e previsões em memória.");
                    Console.WriteLine("  ajuda      - Mostra esta lista de comandos.");
                    Console.WriteLine("----------------------------\n");
                }
                else
                {
                    Console.WriteLine($"[Servidor] Comando desconhecido: '{cmd}'. Escreva 'ajuda' para ver a lista de comandos.");
                }
            }
        }

        // Lê o URL do serviço de Análise a partir do appsettings.json (secção AnalysisService:Url),
        // dando precedência à variável de ambiente ANALYSIS_SERVICE_URL como override.
        private static string CarregarAnalysisServiceUrl()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            return Environment.GetEnvironmentVariable("ANALYSIS_SERVICE_URL")
                ?? config["AnalysisService:Url"]
                ?? "http://localhost:50052";
        }

        private static void InicializarClienteAnalise()
        {
            try
            {
                _analysisChannel = GrpcChannel.ForAddress(AnalysisServiceUrl);
                _analysisClient = new AnalysisService.AnalysisServiceClient(_analysisChannel);
                Console.WriteLine($"[Servidor] Cliente gRPC de Análise inicializado para: {AnalysisServiceUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Falha ao inicializar o cliente gRPC de Análise: {ex.Message}");
            }
        }

        private void ExecutarAnalise(string tipo, string zona, string sensorId, string dateFrom, string dateTo)
        {
            if (_analysisClient == null)
            {
                Console.WriteLine("[ERRO] Cliente gRPC de Análise não está inicializado.");
                return;
            }

            var request = new AnalysisRequest
            {
                Type = tipo ?? "",
                Zone = zona ?? "",
                SensorId = sensorId ?? "",
                DateFrom = dateFrom ?? "",
                DateTo = dateTo ?? ""
            };

            // Recolher as leituras do store interno do Servidor e enviá-las no request.
            // O serviço de Análise é puro: não conhece a BD, apenas calcula sobre os dados recebidos.
            var medicoes = _dataStore.ObterMedicoes(tipo, zona, sensorId, dateFrom, dateTo);
            foreach (var m in medicoes)
            {
                request.Readings.Add(new Reading
                {
                    Value = m.Valor,
                    Timestamp = m.Timestamp ?? "",
                    Type = m.Tipo ?? "",
                    SensorId = m.SensorId ?? "",
                    Zone = m.Zona ?? ""
                });
            }
            Console.WriteLine($"[Servidor] {request.Readings.Count} leitura(s) recolhida(s) do store interno para análise.");

            Console.WriteLine($"[Servidor] A enviar pedido de análise ao AnalysisService gRPC (com resiliência Polly)...");
            try
            {
                // Envolver a chamada gRPC no pipeline Polly (retry exponencial 1/2/4s).
                AnalysisResult response = _grpcPipeline.Execute(() => _analysisClient.Analyze(request));

                Console.WriteLine("\n╔══════════════════════════════════════════════╗");
                Console.WriteLine("║            RESULTADO DA ANÁLISE              ║");
                Console.WriteLine("╠══════════════════════════════════════════════╣");
                Console.WriteLine($"║ Média Calculada: {response.ComputedAverage,27:F2} ║");
                Console.WriteLine($"║ Nível de Alerta: {response.AlertLevel,27} ║");
                Console.WriteLine($"║ Timestamp:       {response.Timestamp,27} ║");
                Console.WriteLine("╠══════════════════════════════════════════════╣");
                Console.WriteLine($"  Sumário: {response.ResultSummary}");
                Console.WriteLine("╚══════════════════════════════════════════════╝\n");

                GuardarResultadoAnalise(response, request);
            }
            catch (Exception ex)
            {
                // Falha após todas as tentativas: regista e devolve um resultado de falha
                // (sem rebentar o programa).
                Console.WriteLine($"[ERRO gRPC] Falha ao executar análise via gRPC após retentativas: {ex.Message}");
                var falha = new AnalysisResult
                {
                    ResultSummary = $"FALHA: serviço de Análise indisponível após 3 tentativas ({ex.Message}).",
                    ComputedAverage = 0.0,
                    AlertLevel = "UNKNOWN",
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
                GuardarResultadoAnalise(falha, request);
            }
        }

        // Aceita apenas "linear" ou "ewma"; qualquer outro valor (ou vazio) recai em "linear".
        private static string NormalizarEstrategia(string? input)
        {
            string s = (input ?? "").Trim().ToLower();
            return s == "ewma" ? "ewma" : "linear";
        }

        private void ExecutarPrevisao(string tipo, string zona, int periodos, string strategy = "linear")
        {
            if (_analysisClient == null)
            {
                Console.WriteLine("[ERRO] Cliente gRPC de Análise não está inicializado.");
                return;
            }

            var request = new PredictionRequest
            {
                Type = tipo ?? "",
                Zone = zona ?? "",
                PeriodsToPredict = periodos,
                Strategy = strategy
            };

            // Recolher o histórico do store interno e enviá-lo no request.
            var medicoes = _dataStore.ObterMedicoes(tipo, zona);
            foreach (var m in medicoes)
            {
                request.Readings.Add(new Reading
                {
                    Value = m.Valor,
                    Timestamp = m.Timestamp ?? "",
                    Type = m.Tipo ?? "",
                    SensorId = m.SensorId ?? "",
                    Zone = m.Zona ?? ""
                });
            }
            Console.WriteLine($"[Servidor] {request.Readings.Count} leitura(s) histórica(s) recolhida(s) para previsão.");

            Console.WriteLine($"[Servidor] A enviar pedido de previsão ao AnalysisService gRPC (com resiliência Polly)...");
            try
            {
                PredictionResult response = _grpcPipeline.Execute(() => _analysisClient.Predict(request));

                Console.WriteLine("\n╔══════════════════════════════════════════════╗");
                Console.WriteLine("║            RESULTADO DA PREVISÃO             ║");
                Console.WriteLine("╠══════════════════════════════════════════════╣");
                Console.WriteLine($"║ Estratégia:      {response.StrategyUsed,27} ║");
                Console.WriteLine($"║ Timestamp:       {response.Timestamp,27} ║");
                Console.WriteLine("╠══════════════════════════════════════════════╣");
                Console.WriteLine($"  Sumário: {response.PredictionSummary}");
                Console.WriteLine("╚══════════════════════════════════════════════╝\n");

                GuardarResultadoPrevisao(response, request);
            }
            catch (Exception ex)
            {
                // Falha após todas as tentativas: regista e devolve um resultado de falha.
                Console.WriteLine($"[ERRO gRPC] Falha ao executar previsão via gRPC após retentativas: {ex.Message}");
                Console.WriteLine($"  Sumário: FALHA: serviço de Análise indisponível após 3 tentativas ({ex.Message}).");

                var falha = new PredictionResult
                {
                    PredictionSummary = $"FALHA: serviço de Análise indisponível após 3 tentativas ({ex.Message}).",
                    StrategyUsed = request.Strategy,
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
                GuardarResultadoPrevisao(falha, request);
            }
        }

        private void GuardarResultadoAnalise(AnalysisResult result, AnalysisRequest request)
        {
            lock (HistoricoLock)
            {
                HistoricoAnalises.Add(result);
            }

            try
            {
                string logFile = "analysis_history.log";
                using (var writer = new StreamWriter(logFile, append: true, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine($"=== ANALISE EFECTUADA EM {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC ===");
                    writer.WriteLine($"Request - Tipo: {request.Type}, Zona: {request.Zone}, Sensor: {request.SensorId}, From: {request.DateFrom}, To: {request.DateTo}");
                    writer.WriteLine($"Result  - Summary: {result.ResultSummary}");
                    writer.WriteLine($"Result  - Average: {result.ComputedAverage:F2}");
                    writer.WriteLine($"Result  - Alert Level: {result.AlertLevel}");
                    writer.WriteLine($"Result  - Timestamp: {result.Timestamp}");
                    writer.WriteLine("==================================================");
                    writer.WriteLine();
                }
                Console.WriteLine($"[Servidor] Resultado da análise guardado em '{logFile}' e em memória.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Falha ao guardar resultado da análise no ficheiro: {ex.Message}");
            }
        }

        private void GuardarResultadoPrevisao(PredictionResult result, PredictionRequest request)
        {
            lock (HistoricoLock)
            {
                HistoricoPrevisoes.Add(result);
            }

            try
            {
                string logFile = "prediction_history.log";
                using (var writer = new StreamWriter(logFile, append: true, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine($"=== PREVISAO EFECTUADA EM {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC ===");
                    writer.WriteLine($"Request - Tipo: {request.Type}, Zona: {request.Zone}, Periodos: {request.PeriodsToPredict}, Estrategia: {request.Strategy}");
                    writer.WriteLine($"Result  - Summary: {result.PredictionSummary}");
                    writer.WriteLine($"Result  - Strategy Used: {result.StrategyUsed}");
                    writer.WriteLine($"Result  - Forecast: {string.Join(", ", result.Forecast)}");
                    writer.WriteLine($"Result  - Timestamp: {result.Timestamp}");
                    writer.WriteLine("==================================================");
                    writer.WriteLine();
                }
                Console.WriteLine($"[Servidor] Resultado da previsão guardado em '{logFile}' e em memória.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Falha ao guardar resultado da previsão no ficheiro: {ex.Message}");
            }
        }

        /// <summary>
        /// Trata a comunicação com um Gateway individual (executa numa thread separada).
        /// Lê mensagens linha-a-linha e responde conforme o protocolo.
        /// </summary>
        private void TratarGateway(TcpClient client)
        {
            string gatewayId = "?";
            string endpoint = client.Client.RemoteEndPoint?.ToString() ?? "desconhecido";
            bool gwConnected = false; // Controlo de sequência: GW_CONNECT deve ser o primeiro comando

            try
            {
                using NetworkStream stream = client.GetStream();
                using StreamReader reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                using StreamWriter writer = new StreamWriter(stream, System.Text.Encoding.UTF8)
                {
                    AutoFlush = true,
                    NewLine = "\n"
                };

                string? linha;
                while ((linha = reader.ReadLine()) != null)
                {
                    linha = linha.Trim();
                    if (string.IsNullOrEmpty(linha))
                        continue;

                    Console.WriteLine($"[<- {gatewayId}] {linha}");

                    string resposta = ProcessarMensagem(linha, ref gatewayId, ref gwConnected);

                    writer.WriteLine(resposta);
                    Console.WriteLine($"[-> {gatewayId}] {resposta}");

                    // Se foi GW_DISCONNECT, terminar o loop
                    if (linha.StartsWith("GW_DISCONNECT"))
                    {
                        break;
                    }
                }
            }
            catch (IOException)
            {
                Console.WriteLine($"[Servidor] Gateway {gatewayId} desligou-se abruptamente.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Servidor] Erro com gateway {gatewayId}: {ex.Message}");
            }
            finally
            {
                // Remover gateway da lista de ligados
                lock (_gwListLock)
                {
                    _gatewaysLigados.Remove(gatewayId);
                }

                client.Close();
                Console.WriteLine($"[Servidor] Ligação encerrada: {gatewayId} ({endpoint})");
            }
        }

        /// <summary>
        /// Processa uma mensagem recebida de um Gateway e devolve a resposta apropriada.
        /// Verifica controlo de sequência: GW_CONNECT deve ser o primeiro comando.
        /// </summary>
        private string ProcessarMensagem(string mensagem, ref string gatewayId, ref bool gwConnected)
        {
            string[] partes = mensagem.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (partes.Length == 0)
                return "ERR_INVALID_DATA";

            string comando = partes[0];

            // Controlo de sequência: apenas GW_CONNECT é aceite antes da autenticação
            if (!gwConnected && comando != "GW_CONNECT")
            {
                Console.WriteLine($"[Servidor] Comando '{comando}' recebido antes de GW_CONNECT — rejeitado.");
                return "ERR_SEQUENCE";
            }

            switch (comando)
            {
                case "GW_CONNECT":
                    return ProcessarGwConnect(partes, ref gatewayId, ref gwConnected);

                case "FORWARD":
                    return ProcessarForward(partes, gatewayId);

                case "FORWARD_AGGREGATED":
                    return ProcessarForwardAggregated(partes, gatewayId);

                case "SENSOR_STATUS":
                    return ProcessarSensorStatus(partes, gatewayId);

                case "GW_DISCONNECT":
                    return ProcessarGwDisconnect(partes, gatewayId);

                default:
                    Console.WriteLine($"[Servidor] Comando desconhecido: {comando}");
                    return "ERR_INVALID_DATA";
            }
        }

        /// <summary>
        /// Processa GW_CONNECT <gateway_id>
        /// Regista o gateway e responde OK_GW_CONNECTED <gw_id>
        /// </summary>
        private string ProcessarGwConnect(string[] partes, ref string gatewayId, ref bool gwConnected)
        {
            // Validar formato: GW_CONNECT <gateway_id>
            if (partes.Length != 2)
            {
                Console.WriteLine("[Servidor] GW_CONNECT: número de parâmetros inválido.");
                return "ERR_INVALID_DATA";
            }

            string gwId = partes[1];

            // Validar que gateway_id é alfanumérico não vazio
            if (string.IsNullOrWhiteSpace(gwId))
            {
                Console.WriteLine("[Servidor] GW_CONNECT: gateway_id vazio.");
                return "ERR_INVALID_DATA";
            }

            // Rejeitar se este gateway já está conectado noutra sessão
            lock (_gwListLock)
            {
                if (_gatewaysLigados.Contains(gwId))
                {
                    Console.WriteLine($"[Servidor] Gateway {gwId} já está conectado — ligação duplicada rejeitada.");
                    return "ERR_ALREADY_CONNECTED";
                }
                _gatewaysLigados.Add(gwId);
            }

            gatewayId = gwId;
            gwConnected = true;

            Console.WriteLine($"[Servidor] Gateway conectado: {gwId}");
            return $"OK_GW_CONNECTED {gwId}";
        }

        /// <summary>
        /// Processa FORWARD <sensor_id> <tipo> <valor> <zona> <timestamp>
        /// Valida os dados e armazena via DataStore.
        /// Distingue entre ERR_INVALID_DATA e ERR_STORAGE_FULL.
        /// </summary>
        private string ProcessarForward(string[] partes, string gatewayId)
        {
            // Validar formato: FORWARD <sensor_id> <tipo> <valor> <zona> <timestamp>
            if (partes.Length != 6)
            {
                Console.WriteLine($"[Servidor] FORWARD: esperados 6 campos, recebidos {partes.Length}.");
                return "ERR_INVALID_DATA";
            }

            string sensorId = partes[1];
            string tipoDado = partes[2];
            string valor = partes[3];
            string zona = partes[4];
            string timestamp = partes[5];

            // Validar sensor_id não vazio
            if (string.IsNullOrWhiteSpace(sensorId))
            {
                Console.WriteLine("[Servidor] FORWARD: sensor_id vazio.");
                return "ERR_INVALID_DATA";
            }

            // Armazenar via DataStore (retorna enum com tipo de resultado)
            ResultadoArmazenamento resultado = _dataStore.ArmazenarMedicao(sensorId, tipoDado, valor, zona, timestamp);

            switch (resultado)
            {
                case ResultadoArmazenamento.Sucesso:
                    return "OK";
                case ResultadoArmazenamento.ErroStorage:
                    return "ERR_STORAGE_FULL";
                default:
                    return "ERR_INVALID_DATA";
            }
        }

        /// <summary>
        /// Processa FORWARD_AGGREGATED <tipo> <valor_media> <zona> <timestamp>
        /// Valida os dados e armazena via DataStore usando um ID virtual "AGREGADOR".
        /// </summary>
        private string ProcessarForwardAggregated(string[] partes, string gatewayId)
        {
            // Validar formato: FORWARD_AGGREGATED <tipo> <valor_media> <zona> <timestamp>
            if (partes.Length != 5)
            {
                Console.WriteLine($"[Servidor] FORWARD_AGGREGATED: esperados 5 campos, recebidos {partes.Length}.");
                return "ERR_INVALID_DATA";
            }

            string tipoDado = partes[1];
            string valor = partes[2];
            string zona = partes[3];
            string timestamp = partes[4];

            // Armazenar usando um ID virtual que representa uma leitura agregada daquela zona
            ResultadoArmazenamento resultado = _dataStore.ArmazenarMedicao("AGREGADO_" + gatewayId, tipoDado, valor, zona, timestamp);

            switch (resultado)
            {
                case ResultadoArmazenamento.Sucesso:
                    return "OK";
                case ResultadoArmazenamento.ErroStorage:
                    return "ERR_STORAGE_FULL";
                default:
                    return "ERR_INVALID_DATA";
            }
        }

        /// <summary>
        /// Processa SENSOR_STATUS <sensor_id> <estado>
        /// Regista a alteração de estado do sensor.
        /// </summary>
        private string ProcessarSensorStatus(string[] partes, string gatewayId)
        {
            // Validar formato: SENSOR_STATUS <sensor_id> <estado>
            if (partes.Length != 3)
            {
                Console.WriteLine($"[Servidor] SENSOR_STATUS: esperados 3 campos, recebidos {partes.Length}.");
                return "ERR_INVALID_DATA";
            }

            string sensorId = partes[1];
            string estado = partes[2];

            // Validar estados possíveis
            if (!EstadosValidos.Contains(estado))
            {
                Console.WriteLine($"[Servidor] SENSOR_STATUS: estado inválido '{estado}'.");
                return "ERR_INVALID_DATA";
            }

            // Registar no DataStore
            _dataStore.RegistarEstadoSensor(sensorId, estado);

            Console.WriteLine($"[Servidor] Sensor {sensorId} (via {gatewayId}): estado -> {estado}");
            return "OK_STATUS_RECEIVED";
        }

        /// <summary>
        /// Processa GW_DISCONNECT <gateway_id>
        /// Valida que o ID corresponde ao gateway conectado e confirma a desconexão.
        /// </summary>
        private string ProcessarGwDisconnect(string[] partes, string gatewayId)
        {
            // Validar formato: GW_DISCONNECT <gateway_id>
            if (partes.Length != 2)
            {
                Console.WriteLine("[Servidor] GW_DISCONNECT: número de parâmetros inválido.");
                return "ERR_INVALID_DATA";
            }

            string gwId = partes[1];

            // Validar que o gateway_id corresponde ao que fez GW_CONNECT
            if (gwId != gatewayId)
            {
                Console.WriteLine($"[Servidor] GW_DISCONNECT: ID '{gwId}' não corresponde ao gateway conectado '{gatewayId}'.");
                return "ERR_INVALID_DATA";
            }

            lock (_gwListLock)
            {
                _gatewaysLigados.Remove(gwId);
            }

            Console.WriteLine($"[Servidor] Gateway desconectado: {gwId}");
            return "OK_GW_DISCONNECT";
        }
    }
}
