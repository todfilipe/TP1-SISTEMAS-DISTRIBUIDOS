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
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

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
        static string preprocessingUrl = "http://localhost:50051";
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
            // Carregar configurações a partir do appsettings.json
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            var config = builder.Build();

            gatewayId = config["Gateway:Id"] ?? "GW1";
            serverIp = config["Gateway:ServerIp"] ?? "127.0.0.1";
            serverPort = int.TryParse(config["Gateway:ServerPort"], out int spVal) ? spVal : 9090;
            int videoPort = int.TryParse(config["Gateway:VideoPort"], out int vpVal) ? vpVal : 8081;
            preprocessingUrl = config["Gateway:PreprocessingUrl"] ?? Environment.GetEnvironmentVariable("PREPROCESSING_SERVICE_URL") ?? "http://localhost:50051";

            string rabbitHost = config["RabbitMQ:Host"] ?? "localhost";
            int rabbitPort = int.TryParse(config["RabbitMQ:Port"], out int rpVal) ? rpVal : 5672;
            string rabbitUserName = config["RabbitMQ:UserName"] ?? "admin";
            string rabbitPassword = config["RabbitMQ:Password"] ?? "admin";
            string rabbitVirtualHost = config["RabbitMQ:VirtualHost"] ?? "onehealth";
            string rabbitExchangeName = config["RabbitMQ:ExchangeName"] ?? "sensors.exchange";

            // Sobrescrita por parâmetros de linha de comando (se fornecidos)
            if (args.Length >= 1) gatewayId = args[0];
            if (args.Length >= 2) serverIp = args[1];
            if (args.Length >= 3 && int.TryParse(args[2], out int p)) serverPort = p;
            if (args.Length >= 4 && int.TryParse(args[3], out int vp)) videoPort = vp;

            Console.WriteLine($"[GATEWAY] A iniciar com ID: {gatewayId}");
            Console.WriteLine($"[CONFIG] Servidor Central: {serverIp}:{serverPort}");
            Console.WriteLine($"[CONFIG] Video Port: {videoPort}");
            Console.WriteLine($"[CONFIG] Preprocessing URL: {preprocessingUrl}");
            Console.WriteLine($"[CONFIG] RabbitMQ: host={rabbitHost}:{rabbitPort}, user={rabbitUserName}, vhost={rabbitVirtualHost}, exchange={rabbitExchangeName}");

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

            // 4. Iniciar Consumidor RabbitMQ para os Sensores
            IConnection rabbitConnection = null;
            IModel rabbitChannel = null;
            try
            {
                var factory = new ConnectionFactory()
                {
                    HostName = rabbitHost,
                    Port = rabbitPort,
                    UserName = rabbitUserName,
                    Password = rabbitPassword,
                    VirtualHost = rabbitVirtualHost
                };

                rabbitConnection = factory.CreateConnection();
                rabbitChannel = rabbitConnection.CreateModel();

                rabbitChannel.ExchangeDeclare(
                    exchange: rabbitExchangeName,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false,
                    arguments: null
                );

                // Criar fila nomeada e durável do gateway (sobrevive a reinícios e acumula mensagens)
                string queueName = $"gateway.{gatewayId}";
                rabbitChannel.QueueDeclare(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null
                );

                // Definir bindings
                List<string> bindings = new List<string>();
                if (args.Length >= 5 && !int.TryParse(args[4], out _))
                {
                    // Caso o utilizador passe wildcards explícitos (ex: ZONA_CENTRO.#,*.TEMP.#)
                    string[] split = args[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var pattern in split)
                    {
                        bindings.Add(pattern);
                    }
                }
                else
                {
                    // Tentar carregar do appsettings.json
                    var configBindings = config.GetSection("Bindings").GetChildren().Select(c => c.Value).Where(v => v != null).ToList();
                    if (configBindings.Count > 0)
                    {
                        foreach (var binding in configBindings)
                        {
                            bindings.Add(binding!);
                        }
                    }
                    else
                    {
                        // Fallback: Por omissão, obter zonas do sensors.csv
                        var uniqueZones = configManager.GetAllSensors()
                            .Select(s => s.Zona)
                            .Where(z => !string.IsNullOrEmpty(z))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        foreach (var zone in uniqueZones)
                        {
                            bindings.Add($"{zone}.#");
                        }
                    }
                }

                foreach (var binding in bindings)
                {
                    rabbitChannel.QueueBind(queueName, rabbitExchangeName, binding);
                    Console.WriteLine($"[GATEWAY] Fila vinculada ao padrão: '{binding}' no exchange '{rabbitExchangeName}'");
                }

                // Limitar o número de mensagens não confirmadas entregues por consumidor
                // (prefetch), evitando sobrecarga do Gateway quando o gRPC está lento.
                rabbitChannel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

                var consumer = new EventingBasicConsumer(rabbitChannel);
                consumer.Received += (model, ea) =>
                {
                    ulong deliveryTag = ea.DeliveryTag;
                    try
                    {
                        var body = ea.Body.ToArray();
                        var message = Encoding.UTF8.GetString(body);
                        bool shouldAck = ProcessarMensagemRabbit(message);
                        
                        if (shouldAck)
                        {
                            rabbitChannel.BasicAck(deliveryTag, multiple: false);
                        }
                        else
                        {
                            // NACK com requeue=true para tentar reprocessar mais tarde
                            rabbitChannel.BasicNack(deliveryTag, multiple: false, requeue: true);
                            Console.WriteLine($"[RABBITMQ] Mensagem NACKed com requeue=true. DeliveryTag: {deliveryTag}");
                            
                            // Dormir ligeiramente para evitar loops super rápidos caso o gRPC esteja fora do ar
                            Thread.Sleep(1000);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERRO CONSUMO] Falha ao tratar callback do RabbitMQ: {ex.Message}");
                        try
                        {
                            // Por segurança, NACK com requeue em caso de exceção imprevista
                            rabbitChannel.BasicNack(deliveryTag, multiple: false, requeue: true);
                        }
                        catch (Exception nackEx)
                        {
                            Console.WriteLine($"[ERRO NACK] Falha ao enviar NACK: {nackEx.Message}");
                        }
                    }
                };

                rabbitChannel.BasicConsume(
                    queue: queueName,
                    autoAck: false, // Confirmação manual
                    consumer: consumer
                );

                Console.WriteLine($"[GATEWAY] A escutar mensagens do RabbitMQ na fila '{queueName}'...");

                // Ciclo principal mantendo o gateway ativo
                while (isRunning)
                {
                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Falha no listener/consumer do RabbitMQ: {ex.Message}");
            }
            finally
            {
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

                try { rabbitChannel?.Close(); } catch { }
                try { rabbitConnection?.Close(); } catch { }
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

        static bool ProcessarMensagemRabbit(string message)
        {
            try
            {
                SensorMessage? msg = null;
                try
                {
                    msg = JsonSerializer.Deserialize<SensorMessage>(message);
                }
                catch (JsonException jsonEx)
                {
                    Console.WriteLine($"[ERRO RABBITMQ] Falha de desserialização JSON (Poison Message): {jsonEx.Message}");
                    return true; // ACK to discard poison message
                }

                if (msg == null)
                {
                    Console.WriteLine("[ERRO RABBITMQ] Mensagem JSON nula (Poison Message).");
                    return true; // ACK to discard
                }

                string sensorId = msg.sensorId;
                string type = msg.type;

                if (string.IsNullOrEmpty(sensorId))
                {
                    Console.WriteLine("[ERRO RABBITMQ] Mensagem sem sensorId (Poison Message).");
                    return true; // ACK to discard
                }

                if (string.Equals(type, "HEARTBEAT", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[RABBITMQ HEARTBEAT] Sensor '{sensorId}' heartbeat recebido.");
                    var sensorHb = configManager?.GetSensor(sensorId);
                    if (sensorHb != null)
                    {
                        if (sensorHb.Estado == "indisponivel" || sensorHb.Estado == "desligado")
                        {
                            configManager?.ChangeSensorStatus(sensorId, "ativo");
                            string recResponseHb = SendToServer($"SENSOR_STATUS {sensorId} ativo");
                            if (recResponseHb != null)
                            {
                                Console.WriteLine($"[GATEWAY] SENSOR_STATUS (ativo após inatividade/HEARTBEAT via RabbitMQ) enviado para '{sensorId}'. Resposta: {recResponseHb}");
                            }
                        }
                        configManager?.UpdateLastSync(sensorId, DateTime.UtcNow);
                    }
                    return true; // ACK
                }
                else if (string.Equals(type, "DISCONNECT", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[RABBITMQ DISCONNECT] Sensor '{sensorId}' solicitou desconexão.");
                    
                    // Atualizar o estado do sensor na configuração CSV para 'desligado'
                    configManager?.ChangeSensorStatus(sensorId, "desligado");

                    // Notificar o Servidor da mudança de estado
                    string statusResponse = SendToServer($"SENSOR_STATUS {sensorId} desligado");
                    if (statusResponse != null)
                    {
                        Console.WriteLine($"[GATEWAY] SENSOR_STATUS enviado ao Servidor para '{sensorId}' (desconexão via RabbitMQ). Resposta: {statusResponse}");
                    }
                    return true; // ACK
                }
                else
                {
                    // Trata-se de dados (TEMP, HUM, etc.)
                    // Validação completa usando DataValidator
                    string validationRaw = BuildTp1RawMessage(msg);
                    DataValidationResult validationResult =
                        DataValidator.ValidateAndProcessData(validationRaw, sensorId, configManager, null);

                    Console.WriteLine(validationResult.LogMessage);

                    if (!validationResult.IsValid)
                    {
                        Console.WriteLine($"[AVISO VALIDAÇÃO] Leitura do sensor '{sensorId}' rejeitada na validação (Poison Message): {validationResult.ErrorCode}");
                        return true; // ACK to discard poison message
                    }

                    // Se ele estava 'indisponivel' ou 'desligado', volta a ficar 'ativo'.
                    var sensor = configManager?.GetSensor(sensorId);
                    if (sensor == null)
                    {
                        Console.WriteLine($"[AVISO VALIDAÇÃO] Sensor '{sensorId}' não encontrado após validação (Poison Message).");
                        return true; // ACK to discard
                    }

                    if (sensor.Estado == "indisponivel" || sensor.Estado == "desligado")
                    {
                        configManager?.ChangeSensorStatus(sensorId, "ativo");
                        string recResponseData = SendToServer($"SENSOR_STATUS {sensorId} ativo");
                        if (recResponseData != null)
                        {
                            Console.WriteLine($"[GATEWAY] SENSOR_STATUS (ativo após inatividade/DATA via RabbitMQ) enviado para '{sensorId}'. Resposta: {recResponseData}");
                        }
                    }

                    // Pré-Processamento gRPC
                    double valorOriginal = msg.value;
                    // A zona vem da mensagem do sensor; se vazia, usar a configurada no sensors.csv
                    string zonaLeitura = !string.IsNullOrEmpty(msg.zone) ? msg.zone : sensor.Zona;
                    string payloadFormat = string.IsNullOrWhiteSpace(msg.rawFormat) ? "UNKNOWN" : msg.rawFormat.ToUpperInvariant();
                    Console.WriteLine($"[GATEWAY gRPC] A normalizar payload rawFormat={payloadFormat} do sensor '{sensorId}'.");
                    var normalized = NormalizeReading(sensorId, type, valorOriginal, msg.unit, msg.timestamp, msg.raw, zonaLeitura);

                    if (normalized == null)
                    {
                        // gRPC Preprocessing failed or service is offline.
                        // We must NACK with requeue so we can try again when it is online!
                        Console.WriteLine($"[RABBITMQ NACK] Falha ao normalizar leitura do sensor '{sensorId}' (gRPC offline). Requeuing...");
                        return false; 
                    }

                    if (!normalized.IsValid)
                    {
                        Console.WriteLine($"[AVISO gRPC] Leitura do sensor '{sensorId}' rejeitada pelo gRPC Preprocessing (Poison/Invalid Message).");
                        return true; // ACK to discard
                    }

                    if (Math.Abs(valorOriginal - normalized.Value) > 0.0001)
                    {
                        Console.WriteLine($"[GATEWAY gRPC] Valor normalizado para '{sensorId}': {valorOriginal} -> {normalized.Value:F2} (unidade original convertida)");
                    }

                    // Reconstruir a mensagem FORWARD com o valor normalizado.
                    // A zona é propagada a partir da NormalizedReading devolvida pelo gRPC
                    // (com fallback para a zona configurada no sensor).
                    string normalizedValueStr = normalized.Value.ToString("F2", CultureInfo.InvariantCulture);
                    string zonaFinal = !string.IsNullOrEmpty(normalized.Zone) ? normalized.Zone : sensor.Zona;
                    string normalizedForwardMsg = $"FORWARD {sensorId} {normalized.Type} {normalizedValueStr} {zonaFinal} {normalized.Timestamp}";

                    leiturasPendentes.Enqueue(normalizedForwardMsg);

                    // Atualiza o last_sync sempre que é processada uma mensagem DATA válida
                    configManager?.UpdateLastSync(sensorId, DateTime.UtcNow);
                    return true; // ACK
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO PROCESSAMENTO RABBIT] Falha inesperada ao processar mensagem: {ex.Message}");
                return false; // NACK with requeue
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

        static NormalizedReading NormalizeReading(string sensorId, string type, double value, string unit, string timestamp, string rawFormat, string zone)
        {
            var request = new RawReading
            {
                SensorId = sensorId,
                Type = type,
                Value = value,
                Unit = unit ?? "",
                Timestamp = timestamp,
                RawFormat = rawFormat ?? "",
                Zone = zone ?? ""
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

        static string BuildTp1RawMessage(SensorMessage msg)
        {
            string value = msg.value.ToString(CultureInfo.InvariantCulture);
            return $"DATA {msg.type} {value} {msg.zone} {msg.timestamp}";
        }
    }

    public class SensorMessage
    {
        public string sensorId { get; set; }
        public string zone { get; set; }
        public string type { get; set; }
        public double value { get; set; }
        public string unit { get; set; }
        public string timestamp { get; set; }
        public string raw { get; set; }
        public string rawFormat { get; set; } = "";
    }
}
