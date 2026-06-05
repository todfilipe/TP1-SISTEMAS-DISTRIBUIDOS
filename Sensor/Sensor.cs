using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;
using System.Text.Json;
using System.Threading;
using RabbitMQ.Client;
using Shared;

namespace Sensor;

/// <summary>
/// Lógica de comunicação do Sensor com o Gateway.
/// Envia medições via RabbitMQ Pub/Sub e streams de vídeo via TCP.
/// Suporta reconexão automática com retry backoff exponencial.
/// </summary>
public class SensorClient : IDisposable
{
    private IConnection? _rabbitConnection;
    private IModel? _rabbitChannel;
    private readonly ConnectionFactory _connectionFactory;
    private readonly object _connectionLock = new object();
    private volatile bool _isReconnecting = false;

    private readonly string _sensorId;
    private readonly string _gatewayHost;
    private readonly int _rabbitPort;
    private string _zone;
    private readonly string _type;
    private readonly List<string> _types;
    private readonly int _intervalSeconds;
    private readonly string _rabbitUser;
    private readonly string _rabbitPass;
    private readonly string _rabbitVHost;
    private readonly string _payloadFormat;
    private readonly string _environmentServiceUrl;

    private bool _connected;
    private bool _typesRegistered;

    // Heartbeat automático em background (usado no modo CLI)
    private Thread? _heartbeatThread;
    private volatile bool _heartbeatRunning;

    // Loop de publicação automática (usado no modo automático)
    private Thread? _publishLoopThread;
    private volatile bool _publishLoopRunning;

    // Lock para thread-safety
    private readonly object _sendLock = new object();

    // Timeout para ligação e leitura (10s conforme protocolo)
    private const int TimeoutMs = 10000;
    private const int HeartbeatIntervalMs = 5000; // 5s entre heartbeats
    private static readonly HttpClient EnvironmentHttpClient = new()
    {
        Timeout = TimeSpan.FromMilliseconds(1000)
    };

    public string SensorId => _sensorId;
    public bool IsConnected => _connected;
    public bool IsTypesRegistered => _typesRegistered;
    public bool IsOperational => _connected && _typesRegistered;
    public string PayloadFormat => _payloadFormat;
    public int RabbitPort => _rabbitPort;

    public static string NormalizePayloadFormat(string? payloadFormat)
    {
        string normalized = (payloadFormat ?? "JSON").Trim().ToUpperInvariant();
        return normalized is "JSON" or "XML" or "CSV" ? normalized : "JSON";
    }

    /// <summary>
    /// Construtor principal para inicialização automática via appsettings.json.
    /// </summary>
    public SensorClient(
        string sensorId, 
        string gatewayHost, 
        int rabbitPort, 
        string zone, 
        string type, 
        int intervalSeconds,
        string rabbitUser = "admin",
        string rabbitPass = "admin",
        string rabbitVHost = "onehealth",
        string payloadFormat = "JSON")
    {
        _sensorId = sensorId;
        _gatewayHost = gatewayHost;
        _rabbitPort = rabbitPort;
        _zone = zone;
        _types = ParseTypes(type);
        _type = _types[0];
        _intervalSeconds = intervalSeconds;
        _rabbitUser = rabbitUser;
        _rabbitPass = rabbitPass;
        _rabbitVHost = rabbitVHost;
        _payloadFormat = NormalizePayloadFormat(payloadFormat);
        _environmentServiceUrl = (Environment.GetEnvironmentVariable("ENVIRONMENT_SERVICE_URL") ?? "http://localhost:8001")
            .Trim()
            .TrimEnd('/');

        _connectionFactory = CreateConnectionFactory();
    }

    private static List<string> ParseTypes(string typeList)
    {
        var types = new List<string>();
        foreach (string rawType in (typeList ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string type = rawType.ToUpperInvariant();
            if (ProtocolConstants.IsValidSensorType(type) && !types.Contains(type, StringComparer.OrdinalIgnoreCase))
            {
                types.Add(type);
            }
        }

        return types.Count > 0 ? types : new List<string> { "TEMP" };
    }

    /// <summary>
    /// Construtor compatível com a CLI interativa original.
    /// </summary>
    public SensorClient(string sensorId, string gatewayHost, int rabbitPort = 5672)
        : this(sensorId, gatewayHost, rabbitPort, "ZONA_CENTRO", "TEMP", 5)
    {
        _zone = DiscoverZone();
    }

    private ConnectionFactory CreateConnectionFactory()
    {
        return new ConnectionFactory()
        {
            HostName = _gatewayHost,
            Port = _rabbitPort,
            UserName = _rabbitUser,
            Password = _rabbitPass,
            VirtualHost = _rabbitVHost
        };
    }

    /// <summary>
    /// Garante que a ligação e o canal do RabbitMQ estão abertos, reconectando com backoff exponencial se necessário.
    /// </summary>
    public bool EnsureConnection()
    {
        if (_rabbitConnection != null && _rabbitConnection.IsOpen && _rabbitChannel != null && _rabbitChannel.IsOpen)
        {
            return true;
        }

        lock (_connectionLock)
        {
            // Dupla verificação após lock
            if (_rabbitConnection != null && _rabbitConnection.IsOpen && _rabbitChannel != null && _rabbitChannel.IsOpen)
            {
                return true;
            }

            _connected = false;

            if (_isReconnecting) return false;
            _isReconnecting = true;

            Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] [RABBITMQ] Ligação perdida ou indisponível. A iniciar tentativas de ligação para {_gatewayHost}:{_rabbitPort}...");

            int delayMs = 2000; // Atraso inicial de 2 segundos
            const int maxDelayMs = 30000; // Atraso máximo de 30 segundos
            int attempts = 0;

            while (_publishLoopRunning || _heartbeatRunning || _isReconnecting)
            {
                try
                {
                    attempts++;
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [RABBITMQ] Tentativa {attempts} de ligação ao RabbitMQ em {_gatewayHost}:{_rabbitPort}...");

                    // Libertar recursos antigos
                    try { _rabbitChannel?.Dispose(); } catch { }
                    try { _rabbitConnection?.Dispose(); } catch { }

                    _rabbitConnection = _connectionFactory.CreateConnection();
                    _rabbitChannel = _rabbitConnection.CreateModel();

                    // Declarar o exchange sensors.exchange como topic
                    _rabbitChannel.ExchangeDeclare(
                        exchange: "sensors.exchange",
                        type: ExchangeType.Topic,
                        durable: true,
                        autoDelete: false,
                        arguments: null
                    );

                    _connected = true;
                    _isReconnecting = false;
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [RABBITMQ] Ligação estabelecida com sucesso na tentativa {attempts}!");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [RABBITMQ] Falha na tentativa {attempts}: {ex.Message}");
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [RABBITMQ] A aguardar {delayMs / 1000}s antes de tentar novamente...");
                    Thread.Sleep(delayMs);
                    delayMs = Math.Min(delayMs * 2, maxDelayMs); // Backoff exponencial
                }
            }

            _isReconnecting = false;
            return false;
        }
    }

    /// <summary>
    /// Estabelece a ligação com o Broker RabbitMQ do Gateway.
    /// </summary>
    public void ConnectTcp()
    {
        EnsureConnection();
    }

    /// <summary>
    /// Envia CONNECT (simulado localmente se a ligação ao RabbitMQ estiver aberta).
    /// </summary>
    public string SendConnect()
    {
        lock (_sendLock)
        {
            if (EnsureConnection())
            {
                _connected = true;
                return $"OK_CONNECTED {_sensorId}";
            }
            return "ERR: Ligação ao RabbitMQ fechada.";
        }
    }

    /// <summary>
    /// Envia REGISTER_TYPES (simulado localmente).
    /// </summary>
    public string SendRegisterTypes(List<string> types)
    {
        if (!_connected)
            return "ERR: Deve enviar CONNECT primeiro.";

        lock (_sendLock)
        {
            _typesRegistered = true;
            return "OK_TYPES_REGISTERED";
        }
    }

    /// <summary>
    /// Envia DATA com uma medição ambiental via RabbitMQ.
    /// </summary>
    public string SendData(string tipo, string valor, string zona, string? timestamp = null)
    {
        if (!EnsureConnection())
            return "ERR: Canal RabbitMQ indisponível.";

        timestamp ??= DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

        double valDouble = 0;
        double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out valDouble);
        string unit = SensorTypes.GetUnitForType(tipo);

        lock (_sendLock)
        {
            try
            {
                string rawPayload = BuildRawPayload(tipo, valDouble, unit, zona, timestamp);
                var msg = new SensorMessage
                {
                    sensorId = _sensorId,
                    zone = zona,
                    type = tipo,
                    value = valDouble,
                    unit = unit,
                    timestamp = timestamp,
                    raw = rawPayload,
                    rawFormat = _payloadFormat
                };

                string json = JsonSerializer.Serialize(msg);
                var body = System.Text.Encoding.UTF8.GetBytes(json);

                var properties = _rabbitChannel!.CreateBasicProperties();
                properties.Persistent = true; // delivery_mode = 2

                _rabbitChannel.BasicPublish(
                    exchange: "sensors.exchange",
                    routingKey: $"{zona}.{tipo}.{_sensorId}",
                    basicProperties: properties,
                    body: body
                );

                return "OK";
            }
            catch (Exception ex)
            {
                _connected = false; // Forçar reconexão na próxima chamada
                return $"ERR: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Envia HEARTBEAT para sinalizar que o sensor está ativo.
    /// </summary>
    public string SendHeartbeat()
    {
        if (!EnsureConnection())
            return "ERR: Canal RabbitMQ indisponível.";

        lock (_sendLock)
        {
            try
            {
                var msg = new SensorMessage
                {
                    sensorId = _sensorId,
                    zone = _zone,
                    type = "HEARTBEAT",
                    value = 0,
                    unit = "n/a",
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                    raw = $"HEARTBEAT {_sensorId}",
                    rawFormat = "TEXT"
                };

                string json = JsonSerializer.Serialize(msg);
                var body = System.Text.Encoding.UTF8.GetBytes(json);

                var properties = _rabbitChannel!.CreateBasicProperties();
                properties.Persistent = true;

                _rabbitChannel.BasicPublish(
                    exchange: "sensors.exchange",
                    routingKey: $"{_zone}.HEARTBEAT.{_sensorId}",
                    basicProperties: properties,
                    body: body
                );

                return "OK";
            }
            catch (Exception ex)
            {
                _connected = false;
                return $"ERR: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Inicia o envio automático de heartbeats em background (a cada 5s).
    /// </summary>
    public void StartHeartbeatAuto()
    {
        if (_heartbeatRunning) return;

        _heartbeatRunning = true;
        _heartbeatThread = new Thread(() =>
        {
            while (_heartbeatRunning)
            {
                try
                {
                    Thread.Sleep(HeartbeatIntervalMs);
                    if (!_heartbeatRunning) break;

                    string resp = SendHeartbeat();
                    if (resp != "OK")
                        Console.WriteLine($"[HEARTBEAT] Resposta inesperada: {resp}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HEARTBEAT] Sensor '{_sensorId}' — erro no envio: {ex.Message}.");
                    _connected = false;
                }
            }
        })
        {
            IsBackground = true,
            Name = $"Heartbeat-{_sensorId}"
        };
        _heartbeatThread.Start();
    }

    /// <summary>
    /// Para o envio automático de heartbeats.
    /// </summary>
    public void StopHeartbeatAuto()
    {
        _heartbeatRunning = false;
    }

    /// <summary>
    /// Loop de Publicação Automática de Leituras.
    /// </summary>
    public void StartAutomaticPublishing()
    {
        if (_publishLoopRunning) return;

        _publishLoopRunning = true;
        _publishLoopThread = new Thread(() =>
        {
            Console.WriteLine($"╔══════════════════════════════════════════════╗");
            Console.WriteLine($"║   SENSOR AUTOMÁTICO — ID: {_sensorId,-18} ║");
            Console.WriteLine($"╠══════════════════════════════════════════════╣");
            Console.WriteLine($"║   Zona:      {_zone,-31} ║");
            Console.WriteLine($"║   Tipos:     {string.Join(",", _types),-31} ║");
            Console.WriteLine($"║   Intervalo: {_intervalSeconds + " segundos",-31} ║");
            Console.WriteLine($"║   Payload:   {_payloadFormat,-31} ║");
            Console.WriteLine($"╚══════════════════════════════════════════════╝");
            Console.WriteLine();

            _connected = true;
            _typesRegistered = true;

            while (_publishLoopRunning)
            {
                try
                {
                    foreach (string type in _types)
                    {
                        double val = GenerateSimulatedValue(type);
                        string valStr = val.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                        string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

                        string resp = SendData(type, valStr, _zone, ts);
                        if (resp == "OK")
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DATA SENT] {_zone}.{type}.{_sensorId} -> {valStr} {SensorTypes.GetUnitForType(type)}");
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [PAYLOAD] rawFormat={_payloadFormat}");
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [AVISO] Falha ao enviar dados ({type}): {resp}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [ERRO NO LOOP] {ex.Message}");
                    _connected = false;
                }

                Thread.Sleep(_intervalSeconds * 1000);
            }
        })
        {
            IsBackground = true,
            Name = $"PublishLoop-{_sensorId}"
        };
        _publishLoopThread.Start();

        // No modo automático, iniciamos também o heartbeat automático para manter o estado ativo no gateway
        StartHeartbeatAuto();
    }

    /// <summary>
    /// Para a publicação automática de leituras.
    /// </summary>
    public void StopAutomaticPublishing()
    {
        _publishLoopRunning = false;
        StopHeartbeatAuto();
    }

    /// <summary>
    /// Envia um stream de vídeo para o Gateway na porta de vídeo (8081).
    /// </summary>
    public string SendVideoStream(int videoPort, int frameCount, int frameIntervalMs = 100)
    {
        if (!IsOperational)
            return "ERR: Deve completar CONNECT e REGISTER_TYPES primeiro.";

        TcpClient? videoClient = null;
        try
        {
            videoClient = new TcpClient();
            var connectTask = videoClient.ConnectAsync(_gatewayHost, videoPort);
            if (!connectTask.Wait(TimeoutMs))
            {
                videoClient.Close();
                return "ERR: Timeout ao ligar à porta de vídeo.";
            }

            var stream = videoClient.GetStream();
            var vWriter = new StreamWriter(stream, System.Text.Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };
            var vReader = new StreamReader(stream, System.Text.Encoding.UTF8);

            // Handshake: VIDEO_STREAM <sensor_id>
            vWriter.WriteLine($"VIDEO_STREAM {_sensorId}");
            string resp = vReader.ReadLine() ?? "ERR: Sem resposta.";

            if (resp != "OK_VIDEO_STARTED")
            {
                videoClient.Close();
                return $"ERR: Handshake vídeo falhou: {resp}";
            }

            Console.WriteLine($"[VIDEO] Stream iniciada ({frameCount} frames)...");

            // Enviar frames
            for (int i = 1; i <= frameCount; i++)
            {
                vWriter.WriteLine($"FRAME {i}");
                Console.WriteLine($"[VIDEO] Frame {i}/{frameCount} enviado.");
                if (i < frameCount)
                    Thread.Sleep(frameIntervalMs);
            }

            // Terminar stream
            vWriter.WriteLine("STREAM_END");
            Console.WriteLine("[VIDEO] Stream terminada.");

            vWriter.Dispose();
            vReader.Dispose();
            videoClient.Close();

            return "OK_VIDEO_STREAM_COMPLETE";
        }
        catch (Exception ex)
        {
            videoClient?.Close();
            return $"ERR: {ex.Message}";
        }
    }

    /// <summary>
    /// Envia DISCONNECT para terminar a comunicação de forma ordenada.
    /// </summary>
    public string SendDisconnect()
    {
        StopAutomaticPublishing();
        StopHeartbeatAuto();

        lock (_sendLock)
        {
            try
            {
                if (_rabbitChannel != null && _rabbitChannel.IsOpen)
                {
                    var msg = new SensorMessage
                    {
                        sensorId = _sensorId,
                        zone = _zone,
                        type = "DISCONNECT",
                        value = 0,
                        unit = "n/a",
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                        raw = $"DISCONNECT {_sensorId}",
                        rawFormat = "TEXT"
                    };

                    string json = JsonSerializer.Serialize(msg);
                    var body = System.Text.Encoding.UTF8.GetBytes(json);

                    var properties = _rabbitChannel.CreateBasicProperties();
                    properties.Persistent = true;

                    _rabbitChannel.BasicPublish(
                        exchange: "sensors.exchange",
                        routingKey: $"{_zone}.DISCONNECT.{_sensorId}",
                        basicProperties: properties,
                        body: body
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO DISCONNECT] Falha ao publicar disconnect: {ex.Message}");
            }

            _connected = false;
            _typesRegistered = false;

            try { _rabbitChannel?.Close(); } catch { }
            try { _rabbitConnection?.Close(); } catch { }

            _rabbitChannel = null;
            _rabbitConnection = null;

            return "OK_DISCONNECT";
        }
    }

    private double GenerateSimulatedValue(string type)
    {
        double? environmentValue = TryGetEnvironmentValue(type);
        return environmentValue ?? GenerateFallbackValue(type);
    }

    private double? TryGetEnvironmentValue(string type)
    {
        if (string.IsNullOrWhiteSpace(_environmentServiceUrl))
            return null;

        try
        {
            string url = $"{_environmentServiceUrl}/reading?zone={Uri.EscapeDataString(_zone)}&type={Uri.EscapeDataString(type)}&sensorId={Uri.EscapeDataString(_sensorId)}";
            using HttpResponseMessage response = EnvironmentHttpClient.GetAsync(url).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
                return null;

            using Stream stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            using JsonDocument document = JsonDocument.Parse(stream);

            if (document.RootElement.TryGetProperty("value", out JsonElement valueElement)
                && valueElement.TryGetDouble(out double value))
            {
                return Math.Round(value, type.Equals("AR", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
            }
        }
        catch
        {
            // Fallback silencioso para manter o sensor operacional se o servico nao estiver disponivel.
        }

        return null;
    }

    private double GenerateFallbackValue(string type)
    {
        var rnd = new Random();
        return type.ToUpper() switch
        {
            "TEMP" => Math.Round(15.0 + rnd.NextDouble() * 15.0, 1),
            "HUM" => Math.Round(45.0 + rnd.NextDouble() * 35.0, 1),
            "RUIDO" => Math.Round(45.0 + rnd.NextDouble() * 45.0, 1),
            "PM2.5" => Math.Round(5.0 + rnd.NextDouble() * 35.0, 1),
            "PM10" => Math.Round(10.0 + rnd.NextDouble() * 80.0, 1),
            "LUZ" => Math.Round(100.0 + rnd.NextDouble() * 800.0, 1),
            "AR" => Math.Round(0.5 + rnd.NextDouble() * 4.0, 2),
            _ => Math.Round(rnd.NextDouble() * 100.0, 1)
        };
    }

    private string DiscoverZone()
    {
        string[] pathsToTry = {
            "sensors.csv",
            "../Gateway/sensors.csv",
            "../../Gateway/sensors.csv",
            "../../../Gateway/sensors.csv",
            "../../../../Gateway/sensors.csv",
            "../Gateway/bin/Debug/net8.0/sensors.csv",
            "../../Gateway/bin/Debug/net8.0/sensors.csv"
        };

        foreach (var path in pathsToTry)
        {
            if (File.Exists(path))
            {
                try
                {
                    var lines = File.ReadAllLines(path);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                            continue;

                        var parts = trimmed.Split(':');
                        if (parts.Length >= 3 && string.Equals(parts[0].Trim(), _sensorId, StringComparison.OrdinalIgnoreCase))
                        {
                            return parts[2].Trim();
                        }
                    }
                }
                catch
                {
                    // Ignorar erros
                }
            }
        }

        return "ZONA_CENTRO";
    }

    private string BuildRawPayload(string tipo, double valor, string unit, string zona, string timestamp)
    {
        return _payloadFormat switch
        {
            "XML" => BuildXmlPayload(tipo, valor, unit, zona, timestamp),
            "CSV" => BuildCsvPayload(tipo, valor, unit, zona, timestamp),
            _ => BuildJsonPayload(tipo, valor, unit, zona, timestamp)
        };
    }

    private string BuildJsonPayload(string tipo, double valor, string unit, string zona, string timestamp)
    {
        return JsonSerializer.Serialize(new
        {
            sensorId = _sensorId,
            zone = zona,
            type = tipo,
            value = valor,
            unit,
            timestamp
        });
    }

    private string BuildXmlPayload(string tipo, double valor, string unit, string zona, string timestamp)
    {
        string value = valor.ToString(CultureInfo.InvariantCulture);
        return "<reading>"
            + $"<sensorId>{EscapeXml(_sensorId)}</sensorId>"
            + $"<zone>{EscapeXml(zona)}</zone>"
            + $"<type>{EscapeXml(tipo)}</type>"
            + $"<value>{EscapeXml(value)}</value>"
            + $"<unit>{EscapeXml(unit)}</unit>"
            + $"<timestamp>{EscapeXml(timestamp)}</timestamp>"
            + "</reading>";
    }

    private string BuildCsvPayload(string tipo, double valor, string unit, string zona, string timestamp)
    {
        string value = valor.ToString(CultureInfo.InvariantCulture);
        return string.Join(",", new[]
        {
            EscapeCsv(_sensorId),
            EscapeCsv(tipo),
            EscapeCsv(value),
            EscapeCsv(unit),
            EscapeCsv(timestamp),
            EscapeCsv(zona)
        });
    }

    private static string EscapeXml(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    public void Dispose()
    {
        StopAutomaticPublishing();
        StopHeartbeatAuto();

        if (_connected)
        {
            try
            {
                SendDisconnect();
            }
            catch
            {
                // Ignorar
            }
        }

        _connected = false;
        _typesRegistered = false;
        _rabbitChannel?.Dispose();
        _rabbitConnection?.Dispose();
    }
}
