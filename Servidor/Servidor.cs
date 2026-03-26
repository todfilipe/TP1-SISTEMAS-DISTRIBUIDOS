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
        private bool _running;

        // Lista de gateways ligados (para referência/log)
        private readonly List<string> _gatewaysLigados = new();
        private readonly object _gwListLock = new();

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
            catch (SocketException ex) when (!_running)
            {
                // Servidor encerrado normalmente
                Console.WriteLine($"[Servidor] Encerrado.");
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
                if (input != null && input.Trim().ToLower() == "sair")
                {
                    Console.WriteLine("[Servidor] A encerrar...");
                    _running = false;
                    _listener?.Stop();
                    break;
                }
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

                    string resposta = ProcessarMensagem(linha, ref gatewayId);

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
        /// </summary>
        private string ProcessarMensagem(string mensagem, ref string gatewayId)
        {
            string[] partes = mensagem.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (partes.Length == 0)
                return "ERR_INVALID_DATA";

            string comando = partes[0];

            switch (comando)
            {
                case "GW_CONNECT":
                    return ProcessarGwConnect(partes, ref gatewayId);

                case "FORWARD":
                    return ProcessarForward(partes, gatewayId);

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
        private string ProcessarGwConnect(string[] partes, ref string gatewayId)
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

            gatewayId = gwId;

            lock (_gwListLock)
            {
                if (!_gatewaysLigados.Contains(gwId))
                {
                    _gatewaysLigados.Add(gwId);
                }
            }

            Console.WriteLine($"[Servidor] Gateway conectado: {gwId}");
            return $"OK_GW_CONNECTED {gwId}";
        }

        /// <summary>
        /// Processa FORWARD <sensor_id> <tipo> <valor> <zona> <timestamp>
        /// Valida os dados e armazena via DataStore.
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

            // Tentar armazenar (DataStore faz as validações de tipo, zona, valor, timestamp)
            string? erro = _dataStore.ArmazenarMedicao(sensorId, tipoDado, valor, zona, timestamp);

            if (erro == null)
            {
                return "OK";
            }
            else
            {
                // Retornar o código de erro específico (ERR_INVALID_DATA ou ERR_STORAGE_FULL)
                return erro;
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
            HashSet<string> estadosValidos = new()
            {
                "ativo", "manutencao", "desativado", "indisponivel", "desligado"
            };

            if (!estadosValidos.Contains(estado))
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
        /// Confirma a desconexão do gateway.
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

            lock (_gwListLock)
            {
                _gatewaysLigados.Remove(gwId);
            }

            Console.WriteLine($"[Servidor] Gateway desconectado: {gwId}");
            return "OK_GW_DISCONNECT";
        }
    }
}
