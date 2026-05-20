using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Grpc.Net.Client;
using Preprocessing;
using Polly;
using Polly.Retry;
using Grpc.Core;

namespace Gateway
{
    // Estados possíveis de um sensor durante a sessão (conforme sd_rel.pdf secção 3.1)
    enum SensorState
    {
        AGUARDA_CONNECT,
        AGUARDA_REGISTER_TYPES,
        OPERACIONAL
    }

    class Program
    {
        static string gatewayId;
        static string serverIp;
        static int serverPort;
        static TcpClient serverClient;
        static StreamReader serverReader;
        static StreamWriter serverWriter;
        static bool isRunning = true;
        static bool isReconnecting = false;

        // Gestor da configuração dos sensores (ficheiro sensors.csv)
        static SensorConfigManager configManager;

        // Monitor de heartbeats dos sensores (deteta timeouts)
        static HeartbeatGateway heartbeatMonitor;

        // Buffer local para retentativa de mensagens falhadas GW→Servidor
        static RetryBuffer retryBuffer;

        // Fila thread-safe (Producer-Consumer) para a agregação global de leituras dos Sensores
        static readonly ConcurrentQueue<string> leiturasPendentes = new ConcurrentQueue<string>();

        // Objeto de sincronização para garantir exclusão mútua na comunicação com o Servidor
        static readonly object serverLock = new object();

        // Mutex nomeado garantido para acesso thread-safe e inter-processos ao ficheiro de vídeo
        static readonly Mutex videoLogMutex = new Mutex(false, "GatewayVideoLogMutex");

        // gRPC Preprocessing configuration
        static readonly string preprocessingUrl = Environment.GetEnvironmentVariable("PREPROCESSING_SERVICE_URL") ?? "http://localhost:50051";
        static GrpcChannel preprocessingChannel;
        static PreprocessingService.PreprocessingServiceClient preprocessingClient;

        // Polly resilience pipeline for gRPC retries
        static readonly ResiliencePipeline<NormalizedReading> preprocessingPipeline = 
            new ResiliencePipelineBuilder<NormalizedReading>()
                .AddRetry(new RetryStrategyOptions<NormalizedReading>
                {
                    ShouldHandle = new PredicateBuilder<NormalizedReading>()
                        .Handle<RpcException>(ex => ex.StatusCode == StatusCode.Unavailable || 
                                                    ex.StatusCode == StatusCode.DeadlineExceeded ||
                                                    ex.StatusCode == StatusCode.Internal)
                        .Handle<Exception>(),
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(1)
                })
                .AddTimeout(TimeSpan.FromSeconds(5))
                .Build();

        // Controlo de sessões ativas por sensor_id (evita sessões duplicadas)
        static readonly HashSet<string> _activeSessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly object _sessionLock = new object();

        static void Main(string[] args)
        {
            // Validação dos parâmetros de arranque
            if (args.Length < 1)
            {
                Console.WriteLine("Uso: Gateway <gateway_id> [server_ip] [server_port] [video_port] [sensor_port]");
                return;
            }

            gatewayId = args[0];
            serverIp = args.Length >= 2 ? args[1] : "127.0.0.1";
            serverPort = args.Length >= 3 && int.TryParse(args[2], out int p) ? p : 9090;
            int videoPort = args.Length >= 4 && int.TryParse(args[3], out int vp) ? vp : 8081;
            int sensorPort = args.Length >= 5 && int.TryParse(args[4], out int sp) ? sp : 8080;

            Console.WriteLine($"[GATEWAY] A iniciar com ID: {gatewayId}");

            // Inicializar cliente gRPC
            InitializeGrpcClients();

            // 1. Ligar ao Servidor
            try
            {
                serverClient = new TcpClient(serverIp, serverPort);
                var stream = serverClient.GetStream();

                // Inicializar leitores/escritores (UTF-8) com AutoFlush no StreamWriter
                serverReader = new StreamReader(stream, Encoding.UTF8);
                serverWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };

                // Enviar a mensagem inicial de conexão ao Servidor
                serverWriter.WriteLine($"GW_CONNECT {gatewayId}");
                Console.WriteLine($"[GATEWAY] Pedido de ligação enviado ao Servidor (GW_CONNECT {gatewayId}).");

                // Aguardar a resposta de confirmação (formato: OK_GW_CONNECTED <gw_id>)
                string response = serverReader.ReadLine();
                if (response == null || !response.StartsWith("OK_GW_CONNECTED"))
                {
                    Console.WriteLine($"[ERRO] Falha ao ligar ao Servidor. Resposta obtida: {response}");
                    return;
                }

                Console.WriteLine("[GATEWAY] Ligado com sucesso ao Servidor (recebido OK_GW_CONNECTED).");

                // 1.1 Carregar a configuração dos sensores a partir do ficheiro CSV
                string csvPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\sensors.csv"));
                configManager = new SensorConfigManager(csvPath);
                int loaded = configManager.LoadConfig();
                Console.WriteLine($"[GATEWAY] Configuração de sensores carregada ({loaded} sensor(es)).");

                // 1.2 Iniciar o buffer de retentativa (mensagens falhadas GW→Servidor)
                retryBuffer = new RetryBuffer(SendToServer);
                retryBuffer.Start();

                // 1.3 Iniciar o monitor de heartbeats (deteta sensores com timeout)
                heartbeatMonitor = new HeartbeatGateway(configManager, SendToServer);
                heartbeatMonitor.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Não foi possível ligar ao Servidor ({serverIp}:{serverPort}): {ex.Message}");
                return;
            }

            // 2. Evento para Terminação Limpa (Ctrl+C)
            // Permite enviar a mensagem de desconexão antes de encerrar o gateway
            Console.CancelKeyPress += (sender, e) =>
            {
                Console.WriteLine("\n[GATEWAY] A encerrar Gateway...");
                e.Cancel = true; // Impede terminação imediata
                isRunning = false;
                retryBuffer?.Stop();
                heartbeatMonitor?.Stop();

                lock (serverLock)
                {
                    try
                    {
                        serverWriter?.WriteLine($"GW_DISCONNECT {gatewayId}");
                        Console.WriteLine($"[GATEWAY] Mensagem GW_DISCONNECT enviada ao Servidor.");
                    }
                    catch { }
                }

                Environment.Exit(0);
            };

            // 3. Abrir Listener para streams de vídeo na porta 8081 (thread separada)
            Thread videoThread = new Thread(() =>
            {
                TcpListener videoListener = null;
                try
                {
                    videoListener = new TcpListener(IPAddress.Any, videoPort);
                    videoListener.Start();
                    Console.WriteLine($"[GATEWAY] A escutar streams de vídeo na porta {videoPort}...");

                    while (isRunning)
                    {
                        if (videoListener.Pending())
                        {
                            TcpClient videoClient = videoListener.AcceptTcpClient();
                            Thread handler = new Thread(() => HandleVideoStream(videoClient));
                            handler.IsBackground = true;
                            handler.Start();
                        }
                        else
                        {
                            Thread.Sleep(100);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO] Falha no listener de vídeo: {ex.Message}");
                }
                finally
                {
                    videoListener?.Stop();
                }
            });
            videoThread.IsBackground = true;
            videoThread.Start();

            // Inicializar Thread do Agregador (Producer-Consumer final)
            Thread threadAgregador = new Thread(ProcessarAgregacao);
            threadAgregador.IsBackground = true;
            threadAgregador.Start();

            // Iniciar o listener TCP para os Sensores na porta 8080
            TcpListener sensorListener = null;
            try
            {
                sensorListener = new TcpListener(IPAddress.Any, sensorPort);
                sensorListener.Start();
                Console.WriteLine($"[GATEWAY] A escutar Sensores na porta {sensorPort}...");

                // Ciclo principal que aceita novos sensores concorrentemente
                while (isRunning)
                {
                    if (sensorListener.Pending())
                    {
                        // Aceita um sensor e cria uma Thread para o seu atendimento
                        TcpClient sensorClient = sensorListener.AcceptTcpClient();
                        Thread sensorThread = new Thread(() => HandleSensor(sensorClient));
                        // Marcar como background para não impedir o encerramento do processo
                        sensorThread.IsBackground = true;
                        sensorThread.Start();
                    }
                    else
                    {
                        // Pausa curta para evitar consumo de CPU a 100%
                        Thread.Sleep(100);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Falha no listener de Sensores: {ex.Message}");
            }
            finally
            {
                sensorListener?.Stop();
                retryBuffer?.Stop();
                heartbeatMonitor?.Stop();

                // Enviar GW_DISCONNECT antes de fechar a ligação ao Servidor
                lock (serverLock)
                {
                    try
                    {
                        serverWriter?.WriteLine($"GW_DISCONNECT {gatewayId}");
                        Console.WriteLine($"[GATEWAY] GW_DISCONNECT enviado ao Servidor no encerramento.");
                    }
                    catch
                    {
                        // Servidor pode já estar desligado — ignorar
                    }
                }

                serverClient?.Close();
            }
        }

        // ─────────────────────────────────────────────────────────
        // ProcessarAgregacao: Lógica da Thread de Agregação
        // ─────────────────────────────────────────────────────────

        static void ProcessarAgregacao()
        {
            while (true)
            {
                // Agrupar a cada 15 segundos
                Thread.Sleep(15000);

                // Puxar todas as mensagens atuais da queue
                List<string> lote = new List<string>();
                while (leiturasPendentes.TryDequeue(out string forwardMsg))
                {
                    lote.Add(forwardMsg);
                }

                if (lote.Count == 0) continue;

                Console.WriteLine($"\n[AGREGADOR] A processar {lote.Count} mensagens recebidas nos últimos 15s...");

                // Estrutura para agrupar: Chave(Tipo|Zona) -> Lista de Valores Numéricos
                Dictionary<string, List<double>> agregados = new Dictionary<string, List<double>>();

                foreach (string msg in lote)
                {
                    // Formato anterior: FORWARD S101 TEMP 22.5 ZONA_CENTRO 2026-04-17T12:00:00
                    string[] partes = msg.Split(' ');
                    if (partes.Length < 6)
                    {
                        Console.WriteLine($"[AGREGADOR] AVISO: mensagem descartada por formato inválido (esperados >=6 blocos, recebidos {partes.Length}). Mensagem raw: '{msg}'");
                        continue;
                    }

                    string tipo = partes[2];
                    string valorStr = partes[3];
                    string zona = partes[4];

                    if (double.TryParse(valorStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double valor))
                    {
                        string chave = $"{tipo}|{zona}";
                        if (!agregados.ContainsKey(chave)) agregados[chave] = new List<double>();
                        agregados[chave].Add(valor);
                    }
                }

                // Enviar as médias calculadas
                string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
                foreach (var kvp in agregados)
                {
                    string[] chavePartes = kvp.Key.Split('|');
                    string tipo = chavePartes[0];
                    string zona = chavePartes[1];

                    // Calcula a média literal da zona para este tipo
                    double soma = 0;
                    foreach(double v in kvp.Value) soma += v;
                    double media = soma / kvp.Value.Count;

                    string mediaStr = media.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                    
                    // Em vez de "FORWARD", constrói o pacote reduzido de agregação
                    // FORWARD_AGGREGATED <tipo> <valor_media> <zona> <timestamp>
                    string pacoteAgregado = $"FORWARD_AGGREGATED {tipo} {mediaStr} {zona} {ts}";

                    // SendToServer garante que se o server falhar, vai parar à RetryQueue do próprio Gateway
                    string resposta = SendToServer(pacoteAgregado);
                    
                    if (resposta == null)
                    {
                        // O servidor não respondeu = Falha de ligação. Retentamos mais tarde.
                        Console.WriteLine($"[AVISO] Servidor Central falhou a ligação. Agregado protegido no buffer/disco: {pacoteAgregado}");
                        retryBuffer.Enqueue(pacoteAgregado);
                    }
                    else if (!resposta.StartsWith("OK"))
                    {
                        // O servidor respondeu, mas rejeitou a leitura ativamente (Ex: ERR_INVALID_DATA).
                        // Logo, a mensagem tem "lixo" ou está viciada. Temos de a DEITAR FORA!
                        Console.WriteLine($"[Descartado] O Servidor Central vetou ativamente os dados agregados! (Resposta: {resposta})");
                    }
                    else
                    {
                        Console.WriteLine($"[AGREGADOR] DB OK -> {pacoteAgregado}");
                    }
                }
            }
        }

        /// <summary>
        /// Envia uma mensagem ao Servidor e lê a resposta, tudo dentro do lock.
        /// Retorna a resposta do Servidor, ou null em caso de erro.
        /// </summary>
        static string SendToServer(string message)
        {
            lock (serverLock)
            {
                if (isReconnecting) return null; // Evita tentar enviar enquanto recupera ligação

                try
                {
                    if (serverClient == null || !serverClient.Connected)
                        throw new Exception("Socket fechada.");

                    serverWriter.WriteLine(message);
                    string response = serverReader.ReadLine();
                    if (response == null) throw new Exception("Conexão fechada prematuramente pelo Servidor (EOF).");
                    return response;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO] Falha na comunicação com o Servidor: {ex.Message}");
                    
                    if (!isReconnecting)
                    {
                        isReconnecting = true;
                        Thread reconnectThread = new Thread(ReconnectToServer);
                        reconnectThread.IsBackground = true;
                        reconnectThread.Start();
                    }
                    
                    return null;
                }
            }
        }

        /// <summary>
        /// Loop de recuperação para restabelecer a ligação com o Servidor Central de forma automática (Self-Healing).
        /// </summary>
        static void ReconnectToServer()
        {
            lock (serverLock)
            {
                // Limpar e fechar o TcpClient, StreamReader e StreamWriter antigos.
                serverReader?.Close();
                serverWriter?.Close();
                serverClient?.Close();
            }

            while (isRunning)
            {
                Console.WriteLine($"[GATEWAY] A tentar reconectar ao Servidor em {serverIp}:{serverPort}...");
                try
                {
                    // Tentar estabelecer uma nova ligação TCP ao Servidor Central.
                    TcpClient newClient = new TcpClient(serverIp, serverPort); 
                    var stream = newClient.GetStream();
                    StreamReader newReader = new StreamReader(stream, Encoding.UTF8);
                    StreamWriter newWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };

                    // Refazer o handshake inicial (enviar GW_CONNECT {gatewayId}).
                    newWriter.WriteLine($"GW_CONNECT {gatewayId}");
                    string response = newReader.ReadLine();
                    
                    if (response != null && response.StartsWith("OK_GW_CONNECTED"))
                    {
                        Console.WriteLine("[GATEWAY] Reconectado com sucesso ao Servidor Central.");
                        
                        lock (serverLock)
                        {
                            serverClient = newClient;
                            serverReader = newReader;
                            serverWriter = newWriter;
                            
                            // Ressincronização do estado dos sensores.
                            var dicionario = configManager?.GetDicionarioParaIteracao();
                            if (dicionario != null)
                            {
                                foreach (var kvp in dicionario)
                                {
                                    string id = kvp.Key;
                                    string estado = kvp.Value.Estado;
                                    if (estado != null && estado.ToLower() == "ativo")
                                    {
                                        serverWriter.WriteLine($"SENSOR_STATUS {id} {estado}");
                                        serverReader.ReadLine(); // Consumir possível resposta
                                        Thread.Sleep(20);        // Dormir ~20ms
                                    }
                                }
                            }
                            
                            isReconnecting = false;
                        }
                        break; // Sai do loop de recuperação
                    }
                    else
                    {
                        newClient.Close();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO] Falha na reconexão: {ex.Message}");
                }

                // Em caso de falha (exceção ou não aceite), esperar ~5s e voltar a tentar no loop
                Thread.Sleep(5000);
            }
        }

        /// <summary>
        /// Trata a comunicação com um Sensor de forma independente (numa thread separada).
        /// </summary>
        static void HandleSensor(TcpClient sensorClient)
        {
            string currentSensorId = "UNKNOWN";
            SensorState state = SensorState.AGUARDA_CONNECT;
            const int HandshakeTimeoutMs = 10000; // 10 segundos para completar CONNECT + REGISTER_TYPES
            bool handshakeCompleted = false;
            List<string> sessionTypes = new List<string>();

            // Timer que fecha a ligação se o handshake não completar em 10s
            Timer handshakeTimer = new Timer(_ =>
            {
                if (!handshakeCompleted)
                {
                    Console.WriteLine($"[TIMEOUT] Sensor '{currentSensorId}' não completou o handshake em {HandshakeTimeoutMs / 1000}s — a fechar ligação.");
                    try { sensorClient.Close(); } catch { }
                }
            }, null, HandshakeTimeoutMs, Timeout.Infinite);

            try
            {
                using (var stream = sensorClient.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" })
                {
                    string line;

                    // O ciclo vai decorrer lendo linha a linha enviada pelo sensor
                    while (isRunning && (line = reader.ReadLine()) != null)
                    {
                        // Separa a mensagem por espaços simples
                        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 0) continue;

                        string command = parts[0];

                        switch (command)
                        {
                            case "CONNECT":
                                // CONNECT <sensor_id>
                                if (parts.Length < 2)
                                {
                                    writer.WriteLine("ERR_INVALID_DATA");
                                    break;
                                }

                                if (state != SensorState.AGUARDA_CONNECT)
                                {
                                    writer.WriteLine("ERR_SEQUENCE");
                                    break;
                                }

                                currentSensorId = parts[1];

                                // Validar o sensor contra a configuração CSV
                                SensorConfig sensorCfg = configManager?.GetSensor(currentSensorId);
                                
                                // 1º - O sensor existe sequer no ficheiro? (Se for null, não existe)
                                if (sensorCfg == null)
                                {
                                    Console.WriteLine($"[CONFIG] Sensor '{currentSensorId}' não existe no CSV — ligação rejeitada (ERR_NOT_REGISTERED).");
                                    writer.WriteLine("ERR_NOT_REGISTERED");
                                    return;
                                }

                                // 2º - O sensor existe! Mas está banido ou em manutenção?
                                string estadoActual = sensorCfg.Estado?.ToLower();
                                if (estadoActual == "desativado" || estadoActual == "manutencao")
                                {
                                    Console.WriteLine($"[SENSOR '{currentSensorId}'] rejeitado — estado actual: {sensorCfg.Estado}.");
                                    writer.WriteLine("ERR_SENSOR_INACTIVE");
                                    return;
                                }

                                // 3º - Se chegou aqui, existe e NÃO está banido. Pode entrar!
                                // Verificar se já existe uma sessão ativa para este sensor
                                lock (_sessionLock)
                                {
                                    if (_activeSessions.Contains(currentSensorId))
                                    {
                                        Console.WriteLine($"[GATEWAY] Sessão duplicada rejeitada para '{currentSensorId}'.");
                                        writer.WriteLine("ERR_ALREADY_CONNECTED");
                                        return;
                                    }
                                    _activeSessions.Add(currentSensorId);
                                }
                                state = SensorState.AGUARDA_REGISTER_TYPES;
                                Console.WriteLine($"[SENSOR '{currentSensorId}'] conectou-se.");
                                writer.WriteLine($"OK_CONNECTED {currentSensorId}");
                                break;

                            case "REGISTER_TYPES":
                                // REGISTER_TYPES <t1,t2,...>
                                if (parts.Length < 2)
                                {
                                    writer.WriteLine("ERR_INVALID_DATA");
                                    break;
                                }

                                if (state != SensorState.AGUARDA_REGISTER_TYPES)
                                {
                                    writer.WriteLine("ERR_SEQUENCE");
                                    break;
                                }

                                // AQUI COMEÇA A VERIFICAÇÃO NOVA:
                                string[] tiposEnviados = parts[1].Split(',');
                                SensorConfig configSensor = configManager?.GetSensor(currentSensorId);
                                
                                bool tiposValidos = true;
                                if (configSensor != null)
                                {
                                    foreach (string tipo in tiposEnviados)
                                    {
                                        // Se o sensor enviar um tipo que NÃO está na sua lista do CSV, chumba!
                                        if (!configSensor.TiposDados.Contains(tipo, StringComparer.OrdinalIgnoreCase))
                                        {
                                            tiposValidos = false;
                                            break;
                                        }
                                    }
                                }

                                if (!tiposValidos)
                                {
                                    Console.WriteLine($"[AVISO] Sensor '{currentSensorId}' tentou registar tipos não autorizados.");
                                    writer.WriteLine("ERR_TYPE_NOT_SUPPORTED");
                                    break; // Não passa para OPERACIONAL!
                                }
                                // FIM DA VERIFICAÇÃO NOVA

                                // Guardar os tipos negociados nesta sessão
                                sessionTypes = new List<string>(tiposEnviados);

                                state = SensorState.OPERACIONAL;
                                handshakeCompleted = true;
                                handshakeTimer.Dispose(); // Handshake concluído, cancelar o timeout
                                Console.WriteLine($"[SENSOR '{currentSensorId}'] registou tipos: {parts[1]}");
                                writer.WriteLine("OK_TYPES_REGISTERED");

                                // Atualiza estado na config e notifica o Servidor de que se ligou
                                configManager?.ChangeSensorStatus(currentSensorId, "ativo");
                                configManager?.UpdateLastSync(currentSensorId, DateTime.UtcNow);
                                string statusConnectResponse = SendToServer($"SENSOR_STATUS {currentSensorId} ativo");
                                if (statusConnectResponse != null)
                                {
                                    Console.WriteLine($"[GATEWAY] SENSOR_STATUS (ativo) enviado ao Servidor para '{currentSensorId}'. Resposta: {statusConnectResponse}");
                                }
                                break;

                            case "DATA":
{
                                // ── Verificação de sequência do protocolo ──
                                // O sensor só pode enviar DATA depois de CONNECT + REGISTER_TYPES
                                if (state != SensorState.OPERACIONAL)
                                {
                                    writer.WriteLine("ERR_SEQUENCE");
                                    break;
                                }

                                Console.WriteLine($"[SENSOR '{currentSensorId}'] recebeu mensagem DATA: {line}");

                                // ── Validação completa (Fase 3) ──
                                // Delega toda a lógica de validação ao DataValidator,
                                // que segue a ordem estrita: Registo → Estado → Tipo → Conteúdo → Sucesso
                                DataValidationResult validationResult =
                                    DataValidator.ValidateAndProcessData(line, currentSensorId, configManager, sessionTypes);

                                // Registar o resultado da validação na consola
                                Console.WriteLine(validationResult.LogMessage);

                                if (!validationResult.IsValid)
                                {
                                    // Enviar o código de erro específico ao sensor
                                    writer.WriteLine(validationResult.ErrorCode);
                                    break;
                                }
                                // Se ele estava 'indisponivel' ou 'desligado', volta a ficar 'ativo'.
                                var sensorAtual = configManager?.GetSensor(currentSensorId);
                                if (sensorAtual != null && (sensorAtual.Estado == "indisponivel" || sensorAtual.Estado == "desligado"))
                                {
                                    configManager?.ChangeSensorStatus(currentSensorId, "ativo");
                                    string recResponseData = SendToServer($"SENSOR_STATUS {currentSensorId} ativo");
                                    if (recResponseData != null)
                                    {
                                        Console.WriteLine($"[GATEWAY] SENSOR_STATUS (ativo após inatividade/DATA) enviado para '{currentSensorId}'. Resposta: {recResponseData}");
                                    }
                                }

                                // ── Encaminhar para o Servidor VIA AGREGADOR ──
                                // Em vez de ligar o socket e enviar agora, colocamos na fila de agregação
                                                                // ── Pré-Processamento gRPC ──
                                double valorOriginal = double.Parse(parts[2], CultureInfo.InvariantCulture);
                                var normalized = NormalizeReading(currentSensorId, parts[1], valorOriginal, "", parts[4], "");

                                if (normalized == null || !normalized.IsValid)
                                {
                                string errCode = (normalized == null) ? "ERR_PREPROCESSING_FAILED" : "ERR_INVALID_DATA";
                                Console.WriteLine($"[AVISO gRPC] Leitura do sensor '{currentSensorId}' rejeitada pelo gRPC Preprocessing.");
                                writer.WriteLine(errCode);
                                break;
                                }

                                if (Math.Abs(valorOriginal - normalized.Value) > 0.0001)
                                {
                                Console.WriteLine($"[GATEWAY gRPC] Valor normalizado para '{currentSensorId}': {valorOriginal} -> {normalized.Value:F2} (unidade original convertida)");
                                }

                                // Reconstruir a mensagem FORWARD com o valor normalizado
                                string normalizedValueStr = normalized.Value.ToString("F2", CultureInfo.InvariantCulture);
                                string normalizedForwardMsg = $"FORWARD {currentSensorId} {normalized.Type} {normalizedValueStr} {parts[3]} {normalized.Timestamp}";

                                leiturasPendentes.Enqueue(normalizedForwardMsg);

                                writer.WriteLine("OK");

                                // Atualiza o last_sync sempre que é enviada uma mensagem DATA válida
                                configManager?.UpdateLastSync(currentSensorId, DateTime.UtcNow);
                                break;
                                }

                            case "HEARTBEAT":
                                // HEARTBEAT <sensor_id> — Fase 3: atualiza last_sync
                                if (state != SensorState.OPERACIONAL)
                                {
                                    writer.WriteLine("ERR_SEQUENCE");
                                    break;
                                }
                                Console.WriteLine($"[SENSOR '{currentSensorId}'] heartbeat recebido.");
                                var sensorHb = configManager?.GetSensor(currentSensorId);
                                if (sensorHb != null && (sensorHb.Estado == "indisponivel" || sensorHb.Estado == "desligado"))
                                {
                                    configManager?.ChangeSensorStatus(currentSensorId, "ativo");
                                    string recResponseHb = SendToServer($"SENSOR_STATUS {currentSensorId} ativo");
                                    if (recResponseHb != null)
                                    {
                                        Console.WriteLine($"[GATEWAY] SENSOR_STATUS (ativo após inatividade/HEARTBEAT) enviado para '{currentSensorId}'. Resposta: {recResponseHb}");
                                    }
                                }
                                writer.WriteLine("OK");
                                configManager?.UpdateLastSync(currentSensorId, DateTime.UtcNow);
                                break;

                            case "DISCONNECT":
                                // DISCONNECT <sensor_id>
                                if (parts.Length < 2)
                                {
                                    writer.WriteLine("ERR_INVALID_DATA");
                                    break;
                                }

                                // Verificar se o sensor já fez CONNECT antes de aceitar DISCONNECT
                                if (state == SensorState.AGUARDA_CONNECT)
                                {
                                    writer.WriteLine("ERR_SEQUENCE");
                                    break;
                                }

                                // CORREÇÃO: validar que o ID da mensagem corresponde ao sensor desta sessão
                                if (parts[1] != currentSensorId)
                                {
                                    writer.WriteLine("ERR_INVALID_DATA");
                                    break;
                                }

                                Console.WriteLine($"[SENSOR '{currentSensorId}'] solicitou desconexão.");
                                writer.WriteLine("OK_DISCONNECT");

                                // Atualizar o estado do sensor na configuração CSV para 'desligado'
                                configManager?.ChangeSensorStatus(currentSensorId, "desligado");

                                // Notificar o Servidor da mudança de estado (sd_rel.pdf secção 6.4)
                                string statusResponse = SendToServer($"SENSOR_STATUS {currentSensorId} desligado");
                                if (statusResponse != null)
                                {
                                    Console.WriteLine($"[GATEWAY] SENSOR_STATUS enviado ao Servidor para '{currentSensorId}'. Resposta: {statusResponse}");
                                }

                                return;

                            default:
                                Console.WriteLine($"[AVISO] Comando desconhecido de {currentSensorId}: {line}");
                                writer.WriteLine("ERR_INVALID_DATA");
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO SENSOR '{currentSensorId}'] A conexão caiu de forma inesperada: {ex.Message}");
            }
            finally
            {
                handshakeTimer.Dispose();
                sensorClient.Close();
                // Remover a sessão ativa para este sensor
                lock (_sessionLock)
                {
                    _activeSessions.Remove(currentSensorId);
                }
                Console.WriteLine($"[GATEWAY] Atendimento ao SENSOR '{currentSensorId}' finalizado.");
            }
        }
    
        /// <summary>
        /// Trata uma stream de vídeo enviada por um Sensor na porta 8081.
        /// Recebe frames, regista metadados no ficheiro video_metadata.log.
        /// </summary>
        static void HandleVideoStream(TcpClient client)
        {
            string sensorId = "UNKNOWN";
            DateTime startTime = DateTime.UtcNow;
            int frameCount = 0;

            try
            {
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" })
                {
                    // Ler primeira linha: VIDEO_STREAM <sensor_id>
                    string firstLine = reader.ReadLine();
                    if (firstLine == null)
                        return;

                    string[] parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || parts[0] != "VIDEO_STREAM")
                    {
                        // Formato inválido — fechar sem responder
                        return;
                    }

                    sensorId = parts[1];

                    // 1. Validação de Registo e Estado
                    SensorConfig config = configManager?.GetSensor(sensorId);
                    if (config == null || string.IsNullOrWhiteSpace(config.Estado) || !config.Estado.Equals("ativo", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[VIDEO] Conexão rejeitada para '{sensorId}': Sensor não registado ou não ativo.");
                        writer.WriteLine("ERR_NOT_REGISTERED");
                        return;
                    }

                    // 2. Validação de Permissão (Tipos de Dados)
                    if (config.TiposDados == null || !config.TiposDados.Contains("VIDEO", StringComparer.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[VIDEO] Conexão rejeitada para '{sensorId}': Não tem permissão para transmitir 'VIDEO'.");
                        writer.WriteLine("ERR_INVALID_TYPE");
                        return;
                    }

                    Console.WriteLine($"[VIDEO] Stream iniciada pelo sensor '{sensorId}'.");

                    // 3. Responder com sucesso (confirmação no canal de vídeo)
                    writer.WriteLine("OK_VIDEO_STARTED");

                    // Reiniciar instante de início após identificação do sensor
                    startTime = DateTime.UtcNow;

                    // Ler frames até STREAM_END ou ligação fechar
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] lineParts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (lineParts.Length == 0)
                            continue;

                        if (lineParts[0] == "FRAME")
                        {
                            frameCount++;
                            Console.WriteLine($"[VIDEO] Sensor '{sensorId}' — frame {(lineParts.Length >= 2 ? lineParts[1] : frameCount.ToString())} recebido.");
                        }
                        else if (lineParts[0] == "STREAM_END")
                        {
                            Console.WriteLine($"[VIDEO] Stream do sensor '{sensorId}' terminada ({frameCount} frames recebidos).");
                            break;
                        }
                    }
                }

                // ── Encaminhar metadados de vídeo ao Servidor Central ──
                string zona = configManager?.GetSensor(sensorId)?.Zona ?? "ZONA_CENTRO";
                string ts = startTime.ToString("yyyy-MM-ddTHH:mm:ss");
                string forwardMsg = $"FORWARD {sensorId} VIDEO {frameCount} {zona} {ts}";

                string resposta = SendToServer(forwardMsg);
                if (resposta == null)
                {
                    Console.WriteLine($"[VIDEO] Servidor inalcançável — mensagem colocada no buffer de retentativa: {forwardMsg}");
                    retryBuffer?.Enqueue(forwardMsg);
                }
                else if (resposta.StartsWith("OK"))
                {
                    Console.WriteLine($"[VIDEO] Servidor aceitou dados de vídeo do sensor '{sensorId}'. Resposta: {resposta}");
                }
                else
                {
                    Console.WriteLine($"[VIDEO] Servidor rejeitou dados de vídeo do sensor '{sensorId}'. Resposta: {resposta}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO VIDEO '{sensorId}'] {ex.Message}");
            }
            finally
            {
                // Sempre guarda metadados, mesmo em caso de falha TCP
                TimeSpan duracao = DateTime.UtcNow - startTime;

                videoLogMutex.WaitOne();
                try
                {
                    string logFile = "video_metadata.log";
                    bool escreverCabecalho = !File.Exists(logFile) || new FileInfo(logFile).Length == 0;

                    using (var logWriter = new StreamWriter(logFile, append: true, Encoding.UTF8))
                    {
                        if (escreverCabecalho)
                            logWriter.WriteLine("sensor_id,inicio_utc,duracao_segundos");

                        logWriter.WriteLine($"{sensorId},{startTime:yyyy-MM-ddTHH:mm:ss},{duracao.TotalSeconds:F2}");
                    }
                }
                finally
                {
                    videoLogMutex.ReleaseMutex();
                }

                Console.WriteLine($"[VIDEO] Metadados do sensor '{sensorId}' registados em video_metadata.log.");
                client.Close();
            }
        }

        static void InitializeGrpcClients()
        {
            try
            {
                preprocessingChannel = GrpcChannel.ForAddress(preprocessingUrl);
                preprocessingClient = new PreprocessingService.PreprocessingServiceClient(preprocessingChannel);
                Console.WriteLine($"[GATEWAY] Cliente gRPC de Pré-processamento inicializado para: {preprocessingUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Falha ao inicializar o cliente gRPC: {ex.Message}");
            }
        }

        static NormalizedReading NormalizeReading(string sensorId, string type, double value, string unit, string timestamp, string rawFormat)
        {
            var request = new RawReading
            {
                SensorId = sensorId,
                Type = type,
                Value = value,
                Unit = unit ?? "",
                Timestamp = timestamp,
                RawFormat = rawFormat ?? ""
            };

            try
            {
                return preprocessingPipeline.Execute(() =>
                {
                    return preprocessingClient.Normalize(request);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO gRPC] Falha catastrófica ao normalizar leitura do sensor {sensorId} após retentativas: {ex.Message}");
                return null;
            }
        }
    }
}
