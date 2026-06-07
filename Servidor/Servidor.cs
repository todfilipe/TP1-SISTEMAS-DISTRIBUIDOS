using Analysis;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using Servidor.Mongo;
using Servidor.Mongo.Repositories;
using Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Servidor
{
    /// <summary>
    /// Servidor central que aceita gateways por TCP legado e por RabbitMQ.
    /// Processa mensagens do protocolo: GW_CONNECT, FORWARD, FORWARD_AGGREGATED, SENSOR_STATUS, GW_DISCONNECT.
    /// Responde com OK/ERR conforme o protocolo definido.
    /// Suporta múltiplas ligações concorrentes (uma thread por Gateway).
    /// </summary>
    public class ServidorTCP
    {
        private readonly int _porta;
        private readonly DataStore _dataStore;
        private readonly ReadingsRepository? _readingsRepository;
        private readonly AnalysesRepository? _analysesRepository;
        private readonly SensorsMetadataRepository? _sensorsMetadataRepository;
        private readonly MongoReadingPersister _mongoPersister;
        private readonly AnalysisOrchestrator _analysisOrchestrator;
        private readonly CliHandler _cliHandler;
        private readonly IConfiguration _configuration;
        private readonly GatewayRabbitMqConsumer? _gatewayRabbitMqConsumer;
        private readonly ServidorHttpApi? _httpApi;
        private TcpListener? _listener;
        private Thread? _fallbackSyncThread;
        private volatile bool _running;

        // Conjunto de gateways ligados (HashSet para lookups eficientes)
        private readonly HashSet<string> _gatewaysLigados = new();
        private readonly object _gwListLock = new();

        public ServidorTCP(int porta = 9090)
        {
            _porta = porta;
            _configuration = LoadConfiguration();
            _dataStore = new DataStore();
            (_, _readingsRepository, _analysesRepository, _sensorsMetadataRepository) = InicializarMongo();
            _mongoPersister = new MongoReadingPersister(_readingsRepository, _analysesRepository, _sensorsMetadataRepository);
            _analysisOrchestrator = new AnalysisOrchestrator(_readingsRepository, _mongoPersister);
            _cliHandler = new CliHandler(this);
            _gatewayRabbitMqConsumer = CriarGatewayRabbitMqConsumer();
            _httpApi = CriarHttpApi();
        }

        internal int Porta => _porta;

        internal bool IsRunning => _running;

        internal ReadingsRepository? ReadingsRepository => _readingsRepository;

        internal AnalysesRepository? AnalysesRepository => _analysesRepository;

        internal SensorsMetadataRepository? SensorsMetadataRepository => _sensorsMetadataRepository;

        internal void Stop()
        {
            _running = false;
            _listener?.Stop();
            _gatewayRabbitMqConsumer?.Dispose();
            _httpApi?.Dispose();
        }

        internal (IReadOnlyList<AnalysisResult> Analises, IReadOnlyList<PredictionResult> Previsoes) ObterHistorico()
        {
            return _analysisOrchestrator.ObterHistorico();
        }

        /// <summary>
        /// Inicia o servidor TCP e aguarda ligações de Gateways.
        /// </summary>
        public void Iniciar()
        {
            _analysisOrchestrator.Initialize();
            _listener = new TcpListener(IPAddress.Any, _porta);
            _listener.Start();
            _running = true;
            IniciarGatewayRabbitMqConsumer();
            IniciarHttpApi();
            IniciarSincronizacaoFallback();

            _cliHandler.ShowBanner();

            // Thread para input do utilizador (comando de encerramento)
            Thread inputThread = new Thread(_cliHandler.Run);
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
                _gatewayRabbitMqConsumer?.Dispose();
                _httpApi?.Dispose();
                _listener?.Stop();
            }
        }

        private static IConfiguration LoadConfiguration()
        {
            return new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();
        }

        private GatewayRabbitMqConsumer? CriarGatewayRabbitMqConsumer()
        {
            string enabledText =
                Environment.GetEnvironmentVariable("SERVER_RABBIT_ENABLED") ??
                _configuration["RabbitMQ:GatewayConsumerEnabled"] ??
                "true";

            if (!bool.TryParse(enabledText, out bool enabled) || !enabled)
            {
                Console.WriteLine("[Servidor][RabbitMQ] Consumidor Gateway->Servidor desativado por configuracao.");
                return null;
            }

            string host = Environment.GetEnvironmentVariable("RABBIT_HOST") ?? _configuration["RabbitMQ:Host"] ?? "localhost";
            int port = int.TryParse(_configuration["RabbitMQ:Port"], out int configPort) ? configPort : 5672;
            port = int.TryParse(Environment.GetEnvironmentVariable("RABBIT_PORT"), out int envPort) ? envPort : port;
            string userName = Environment.GetEnvironmentVariable("RABBIT_USER") ?? _configuration["RabbitMQ:UserName"] ?? "admin";
            string password = Environment.GetEnvironmentVariable("RABBIT_PASS") ?? _configuration["RabbitMQ:Password"] ?? "admin";
            string virtualHost = Environment.GetEnvironmentVariable("RABBIT_VHOST") ?? _configuration["RabbitMQ:VirtualHost"] ?? "onehealth";
            string exchangeName =
                Environment.GetEnvironmentVariable("RABBIT_GATEWAY_EXCHANGE") ??
                _configuration["RabbitMQ:GatewayExchangeName"] ??
                "gateway.exchange";
            string queueName =
                Environment.GetEnvironmentVariable("RABBIT_GATEWAY_QUEUE") ??
                _configuration["RabbitMQ:GatewayQueueName"] ??
                "server.gateway.ingest";

            return new GatewayRabbitMqConsumer(host, port, userName, password, virtualHost, exchangeName, queueName, this);
        }

        private void IniciarGatewayRabbitMqConsumer()
        {
            if (_gatewayRabbitMqConsumer == null)
            {
                return;
            }

            Thread startThread = new Thread(() =>
            {
                int maxRetries = 10;
                int delaySeconds = 5;
                for (int i = 1; i <= maxRetries; i++)
                {
                    try
                    {
                        _gatewayRabbitMqConsumer.Start();
                        return; // Connected successfully!
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Servidor][RabbitMQ][Tentativa {i}/{maxRetries}] Nao foi possivel iniciar consumidor Gateway->Servidor: {ex.Message}");
                        if (i < maxRetries)
                        {
                            Thread.Sleep(TimeSpan.FromSeconds(delaySeconds));
                        }
                    }
                }
                Console.WriteLine("[Servidor][RabbitMQ] Falha definitiva ao ligar ao RabbitMQ.");
            })
            {
                IsBackground = true,
                Name = "RabbitConsumerStarter"
            };
            startThread.Start();
        }

        private ServidorHttpApi? CriarHttpApi()
        {
            string enabledText =
                Environment.GetEnvironmentVariable("SERVER_HTTP_ENABLED") ??
                _configuration["HttpApi:Enabled"] ??
                "true";

            if (!bool.TryParse(enabledText, out bool enabled) || !enabled)
            {
                Console.WriteLine("[Servidor][HTTP] API HTTP desativada por configuracao.");
                return null;
            }

            int port = int.TryParse(_configuration["HttpApi:Port"], out int configPort) ? configPort : 9091;
            port = int.TryParse(Environment.GetEnvironmentVariable("SERVER_HTTP_PORT"), out int envPort) ? envPort : port;
            return new ServidorHttpApi(this, port);
        }

        private void IniciarHttpApi()
        {
            try
            {
                _httpApi?.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Servidor][HTTP] Nao foi possivel iniciar API HTTP: {ex.Message}");
            }
        }

        private void IniciarSincronizacaoFallback()
        {
            _fallbackSyncThread = new Thread(SincronizarFallbackLoop)
            {
                IsBackground = true,
                Name = "FallbackSqliteSync"
            };
            _fallbackSyncThread.Start();
            Console.WriteLine("[Servidor][Fallback] Sincronizacao SQLite -> MongoDB iniciada.");
        }

        private void SincronizarFallbackLoop()
        {
            while (_running)
            {
                try
                {
                    SincronizarLeiturasPendentes();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Servidor][Fallback] Erro inesperado na sincronizacao: {ex.Message}");
                }

                Thread.Sleep(TimeSpan.FromSeconds(10));
            }
        }

        private void SincronizarLeiturasPendentes()
        {
            IReadOnlyList<PendingReadingRecord> pendentes = _dataStore.ObterLeiturasPendentes(limit: 100);
            if (pendentes.Count == 0)
            {
                return;
            }

            Console.WriteLine($"[Servidor][Fallback] A tentar sincronizar {pendentes.Count} leitura(s) pendente(s) para MongoDB.");

            foreach (PendingReadingRecord leitura in pendentes)
            {
                _dataStore.RegistarTentativaSincronizacao(leitura.Id);

                bool mongoPersisted = _mongoPersister.PersistirLeituraMongoAsync(
                    leitura.SensorId,
                    leitura.TipoDado,
                    leitura.Valor,
                    leitura.Zona,
                    leitura.Timestamp,
                    leitura.GatewayId,
                    leitura.OriginalMessage,
                    leitura.MessageId).GetAwaiter().GetResult();

                if (!mongoPersisted)
                {
                    Console.WriteLine($"[Servidor][Fallback] MongoDB ainda indisponivel; leitura pendente mantida no SQLite: id={leitura.Id}.");
                    break;
                }

                _dataStore.MarcarLeituraPendenteSincronizada(leitura.Id);
                Console.WriteLine($"[Servidor][Fallback] Leitura pendente sincronizada: id={leitura.Id}, messageId={leitura.MessageId}");
            }
        }

        private static (MongoDbContext? Context, ReadingsRepository? Readings, AnalysesRepository? Analyses, SensorsMetadataRepository? SensorsMetadata) InicializarMongo()
        {
            try
            {
                var context = new MongoDbContext();
                var readings = new ReadingsRepository(context);
                var analyses = new AnalysesRepository(context);
                var sensorsMetadata = new SensorsMetadataRepository(context);

                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    context.Database.RunCommand<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cts.Token);
                    Console.WriteLine($"[Servidor][MongoDB] Repositórios inicializados para a base de dados '{context.DatabaseName}'.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AVISO][MongoDB] MongoDB indisponível no arranque: {ex.Message}. SQLite continua ativo.");
                }

                return (context, readings, analyses, sensorsMetadata);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AVISO][MongoDB] Repositórios MongoDB desativados: {ex.Message}. SQLite continua ativo.");
                return (null, null, null, null);
            }
        }

        internal AnalysisResult? ExecutarAnalise(string tipo, string zona, string sensorId, string dateFrom, string dateTo)
        {
            return _analysisOrchestrator.ExecutarAnalise(tipo, zona, sensorId, dateFrom, dateTo);
        }

        internal AnalysisExecutionResult? ExecutarAnaliseComPersistencia(string tipo, string zona, string sensorId, string dateFrom, string dateTo)
        {
            return _analysisOrchestrator.ExecutarAnaliseComPersistencia(tipo, zona, sensorId, dateFrom, dateTo);
        }

        internal static string NormalizarEstrategia(string? input)
        {
            return ProtocolHelpers.NormalizarEstrategia(input);
        }

        internal PredictionResult? ExecutarPrevisao(string tipo, string zona, int periodos, string strategy = "linear")
        {
            return _analysisOrchestrator.ExecutarPrevisao(tipo, zona, periodos, strategy);
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
                    return ProcessarForward(partes, gatewayId, messageId: null);

                case "FORWARD_AGGREGATED":
                    return ProcessarForwardAggregated(partes, gatewayId, messageId: null);

                case "SENSOR_STATUS":
                    return ProcessarSensorStatus(partes, gatewayId);

                case "GW_DISCONNECT":
                    return ProcessarGwDisconnect(partes, gatewayId);

                default:
                    Console.WriteLine($"[Servidor] Comando desconhecido: {comando}");
                    return "ERR_INVALID_DATA";
            }
        }

        internal string ProcessarMensagemRabbit(string gatewayId, string mensagem, string? messageId = null)
        {
            string[] partes = mensagem.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (partes.Length == 0)
                return "ERR_INVALID_DATA";

            string comando = partes[0];

            if (comando == "GW_CONNECT")
            {
                if (partes.Length != 2 || partes[1] != gatewayId)
                {
                    return "ERR_INVALID_DATA";
                }

                lock (_gwListLock)
                {
                    _gatewaysLigados.Add(gatewayId);
                }

                Console.WriteLine($"[Servidor] Gateway ativo via RabbitMQ: {gatewayId}");
                return $"OK_GW_CONNECTED {gatewayId}";
            }

            if (comando == "GW_DISCONNECT")
            {
                if (partes.Length != 2 || partes[1] != gatewayId)
                {
                    return "ERR_INVALID_DATA";
                }

                lock (_gwListLock)
                {
                    _gatewaysLigados.Remove(gatewayId);
                }

                Console.WriteLine($"[Servidor] Gateway desconectado via RabbitMQ: {gatewayId}");
                return "OK_GW_DISCONNECT";
            }

            lock (_gwListLock)
            {
                _gatewaysLigados.Add(gatewayId);
            }

            switch (comando)
            {
                case "FORWARD":
                    return ProcessarForward(partes, gatewayId, messageId);

                case "FORWARD_AGGREGATED":
                    return ProcessarForwardAggregated(partes, gatewayId, messageId);

                case "SENSOR_STATUS":
                    return ProcessarSensorStatus(partes, gatewayId);

                default:
                    Console.WriteLine($"[Servidor] Comando RabbitMQ desconhecido: {comando}");
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
        private string ProcessarForward(string[] partes, string gatewayId, string? messageId)
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

            ResultadoArmazenamento validacao = _dataStore.ValidarMedicao(sensorId, tipoDado, valor, zona, timestamp, out _);
            if (validacao != ResultadoArmazenamento.Sucesso)
            {
                return validacao == ResultadoArmazenamento.ErroStorage ? "ERR_STORAGE_FULL" : "ERR_INVALID_DATA";
            }

            return PersistirLeituraComFallback(sensorId, tipoDado, valor, zona, timestamp, gatewayId, string.Join(' ', partes), messageId);
        }

        /// <summary>
        /// Processa FORWARD_AGGREGATED <tipo> <valor_media> <zona> <timestamp>
        /// Valida os dados e armazena via DataStore usando um ID virtual "AGREGADOR".
        /// </summary>
        private string ProcessarForwardAggregated(string[] partes, string gatewayId, string? messageId)
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

            string sensorId = "AGREGADO_" + gatewayId;
            ResultadoArmazenamento validacao = _dataStore.ValidarMedicao(sensorId, tipoDado, valor, zona, timestamp, out _);
            if (validacao != ResultadoArmazenamento.Sucesso)
            {
                return validacao == ResultadoArmazenamento.ErroStorage ? "ERR_STORAGE_FULL" : "ERR_INVALID_DATA";
            }

            return PersistirLeituraComFallback(sensorId, tipoDado, valor, zona, timestamp, gatewayId, string.Join(' ', partes), messageId);
        }

        private string PersistirLeituraComFallback(
            string sensorId,
            string tipoDado,
            string valor,
            string zona,
            string timestamp,
            string gatewayId,
            string mensagemOriginal,
            string? messageId)
        {
            bool mongoPersisted = _mongoPersister.PersistirLeituraMongoAsync(
                sensorId,
                tipoDado,
                valor,
                zona,
                timestamp,
                gatewayId,
                mensagemOriginal,
                messageId).GetAwaiter().GetResult();

            if (mongoPersisted)
            {
                return "OK";
            }

            ResultadoArmazenamento fallback = _dataStore.ArmazenarLeituraPendente(
                messageId,
                sensorId,
                tipoDado,
                valor,
                zona,
                timestamp,
                gatewayId,
                mensagemOriginal);

            if (fallback == ResultadoArmazenamento.Sucesso)
            {
                Console.WriteLine($"[Servidor][Fallback] MongoDB indisponivel; leitura guardada no SQLite local e confirmada ao Gateway {gatewayId}.");
                return "OK";
            }

            Console.WriteLine($"[Servidor][ALERTA] MongoDB e fallback SQLite falharam para Gateway {gatewayId}.");
            return fallback == ResultadoArmazenamento.DadosInvalidos ? "ERR_INVALID_DATA" : "ERR_STORAGE_FULL";
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
            if (!ProtocolConstants.IsValidSensorState(estado))
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

