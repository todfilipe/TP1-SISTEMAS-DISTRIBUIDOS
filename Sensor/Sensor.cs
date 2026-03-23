using System.Net.Sockets;

namespace Sensor;

/// <summary>
/// Lógica de comunicação TCP do Sensor com o Gateway.
/// Implementa o protocolo textual linha-a-linha (mensagens terminadas por \n).
/// </summary>
public class SensorClient : IDisposable
{
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    private readonly string _sensorId;
    private readonly string _gatewayHost;
    private readonly int _gatewayPort;

    private bool _connected;
    private bool _typesRegistered;

    // Timeout para ligação e leitura (10s conforme protocolo)
    private const int TimeoutMs = 10000;

    public string SensorId => _sensorId;
    public bool IsConnected => _connected;
    public bool IsTypesRegistered => _typesRegistered;
    public bool IsOperational => _connected && _typesRegistered;

    public SensorClient(string sensorId, string gatewayHost, int gatewayPort = 8080)
    {
        _sensorId = sensorId;
        _gatewayHost = gatewayHost;
        _gatewayPort = gatewayPort;
    }

    /// <summary>
    /// Estabelece a ligação TCP com o Gateway (com timeout).
    /// </summary>
    public void ConnectTcp()
    {
        _client = new TcpClient();

        // Timeout de ligação para não bloquear indefinidamente
        var connectTask = _client.ConnectAsync(_gatewayHost, _gatewayPort);
        if (!connectTask.Wait(TimeoutMs))
        {
            _client.Close();
            throw new TimeoutException($"Timeout ao ligar ao Gateway {_gatewayHost}:{_gatewayPort} ({TimeoutMs}ms).");
        }

        // Timeout de leitura no socket
        _client.ReceiveTimeout = TimeoutMs;

        var stream = _client.GetStream();
        _reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        _writer = new StreamWriter(stream, System.Text.Encoding.UTF8)
        {
            AutoFlush = true,
            NewLine = "\n" // Protocolo define \n como terminador (não \r\n)
        };
    }

    /// <summary>
    /// Envia CONNECT e aguarda resposta OK_CONNECTED ou ERR.
    /// </summary>
    public string SendConnect()
    {
        SendMessage($"CONNECT {_sensorId}");
        string response = ReadResponse();

        // Verificar resposta: OK_CONNECTED <sensor_id>
        if (response == $"OK_CONNECTED {_sensorId}")
            _connected = true;

        return response;
    }

    /// <summary>
    /// Envia REGISTER_TYPES e aguarda resposta OK_TYPES_REGISTERED ou ERR.
    /// </summary>
    public string SendRegisterTypes(List<string> types)
    {
        if (!_connected)
            return "ERR: Deve enviar CONNECT primeiro.";

        string typesStr = string.Join(",", types);
        SendMessage($"REGISTER_TYPES {typesStr}");
        string response = ReadResponse();

        if (response == "OK_TYPES_REGISTERED")
            _typesRegistered = true;

        return response;
    }

    /// <summary>
    /// Envia DATA com uma medição ambiental.
    /// Timestamp em UTC para consistência com o protocolo ISO 8601.
    /// </summary>
    public string SendData(string tipo, string valor, string zona, string? timestamp = null)
    {
        if (!IsOperational)
            return "ERR: Deve completar CONNECT e REGISTER_TYPES primeiro.";

        timestamp ??= DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        SendMessage($"DATA {tipo} {valor} {zona} {timestamp}");
        return ReadResponse();
    }

    /// <summary>
    /// Envia HEARTBEAT para sinalizar que o sensor está ativo.
    /// </summary>
    public string SendHeartbeat()
    {
        if (!_connected)
            return "ERR: Não está conectado.";

        SendMessage($"HEARTBEAT {_sensorId}");
        return ReadResponse();
    }

    /// <summary>
    /// Envia DISCONNECT para terminar a comunicação de forma ordenada.
    /// </summary>
    public string SendDisconnect()
    {
        if (!_connected)
            return "ERR: Não está conectado.";

        SendMessage($"DISCONNECT {_sensorId}");
        string response = ReadResponse();

        if (response == "OK_DISCONNECT")
        {
            _connected = false;
            _typesRegistered = false;
        }

        return response;
    }

    private void SendMessage(string message)
    {
        if (_writer == null)
            throw new InvalidOperationException("Ligação TCP não estabelecida.");

        _writer.WriteLine(message);
    }

    private string ReadResponse()
    {
        if (_reader == null)
            throw new InvalidOperationException("Ligação TCP não estabelecida.");

        string? line = _reader.ReadLine();
        return line ?? "ERR: Ligação fechada pelo Gateway.";
    }

    public void Dispose()
    {
        // Tentar desconexão ordenada se ainda estiver conectado
        if (_connected && _writer != null)
        {
            try
            {
                SendMessage($"DISCONNECT {_sensorId}");
                _reader?.ReadLine(); // Ler OK_DISCONNECT (best-effort)
            }
            catch
            {
                // Ignorar erros durante cleanup — a ligação pode já estar fechada
            }
        }

        _connected = false;
        _typesRegistered = false;
        _writer?.Dispose();
        _reader?.Dispose();
        _client?.Close();
        _client?.Dispose();
    }
}
