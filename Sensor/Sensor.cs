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
    /// Estabelece a ligação TCP com o Gateway.
    /// </summary>
    public void ConnectTcp()
    {
        _client = new TcpClient();
        _client.Connect(_gatewayHost, _gatewayPort);

        var stream = _client.GetStream();
        _reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        _writer = new StreamWriter(stream, System.Text.Encoding.UTF8) { AutoFlush = true };
    }

    /// <summary>
    /// Envia CONNECT e aguarda resposta OK_CONNECTED ou ERR.
    /// </summary>
    public string SendConnect()
    {
        SendMessage($"CONNECT {_sensorId}");
        string response = ReadResponse();

        if (response.StartsWith("OK_CONNECTED"))
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
    /// </summary>
    public string SendData(string tipo, string valor, string zona, string? timestamp = null)
    {
        if (!IsOperational)
            return "ERR: Deve completar CONNECT e REGISTER_TYPES primeiro.";

        timestamp ??= DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
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
        _writer?.Dispose();
        _reader?.Dispose();
        _client?.Close();
        _client?.Dispose();
        _connected = false;
        _typesRegistered = false;
    }
}
