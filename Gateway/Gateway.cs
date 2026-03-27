using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Gateway
{
    /// <summary>
    /// Gateway TCP que:
    /// 1. Liga ao Servidor (porta 9090) e envia GW_CONNECT
    /// 2. Abre TcpListener na porta 8080 para sensores
    /// 3. Lança uma thread por sensor com handshake + sessão
    /// </summary>
    public class GatewayTCP
    {
        private readonly string _gatewayId;
        private readonly int _portaSensores;
        private readonly int _portaServidor;
        private TcpListener? _listenerSensores;
        private TcpClient? _clienteServidor;
        private bool _running;

        public GatewayTCP(string gatewayId, int portaSensores = 8080, int portaServidor = 9090)
        {
            _gatewayId = gatewayId;
            _portaSensores = portaSensores;
            _portaServidor = portaServidor;
        }

        /// <summary>
        /// Inicia o Gateway: liga ao Servidor e depois aceita sensores.
        /// </summary>
        public void Iniciar()
        {
            // 1. Ligar ao Servidor
            if (!LigarAoServidor())
            {
                Console.WriteLine("[Gateway] Não foi possível ligar ao Servidor. A terminar.");
                return;
            }

            // 2. Abrir listener para sensores
            _listenerSensores = new TcpListener(IPAddress.Any, _portaSensores);
            _listenerSensores.Start();
            _running = true;

            Console.WriteLine("╔══════════════════════════════════════════════╗");
            Console.WriteLine("║   GATEWAY — Monitorização Urbana One Health  ║");
            Console.WriteLine("╠══════════════════════════════════════════════╣");
            Console.WriteLine($"║   ID: {_gatewayId}                              ║");
            Console.WriteLine($"║   A escutar sensores na porta {_portaSensores}...       ║");
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
                    if (_listenerSensores.Pending())
                    {
                        TcpClient clienteSensor = _listenerSensores.AcceptTcpClient();
                        string endpoint = clienteSensor.Client.RemoteEndPoint?.ToString() ?? "desconhecido";
                        Console.WriteLine($"[Gateway] Nova ligação: {endpoint}");

                        // Lançar thread para tratar este sensor
                        Thread t = new Thread(() => TratarSensor(clienteSensor));
                        t.IsBackground = true;
                        t.Start();
                    }
                    else
                    {
                        Thread.Sleep(100); // Evitar busy-wait
                    }
                }
            }
            catch (SocketException) when (!_running)
            {
                Console.WriteLine("[Gateway] Encerrado.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gateway] ERRO: {ex.Message}");
            }
            finally
            {
                _listenerSensores?.Stop();
                DesligarDoServidor();
            }
        }

        /// <summary>
        /// Liga ao Servidor via TCP e envia GW_CONNECT.
        /// </summary>
        private bool LigarAoServidor()
        {
            try
            {
                _clienteServidor = new TcpClient();
                _clienteServidor.Connect("127.0.0.1", _portaServidor);

                using NetworkStream stream = _clienteServidor.GetStream();
                StreamReader reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };

                // Enviar GW_CONNECT
                writer.WriteLine($"GW_CONNECT {_gatewayId}");
                Console.WriteLine($"[Gateway] Enviado: GW_CONNECT {_gatewayId}");

                // Aguardar resposta
                string? resposta = reader.ReadLine();
                Console.WriteLine($"[Gateway] Resposta do Servidor: {resposta}");

                if (resposta != null && resposta.StartsWith("OK_GW_CONNECTED"))
                {
                    Console.WriteLine($"[Gateway] Ligação ao Servidor estabelecida com sucesso.");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[Gateway] Servidor rejeitou a ligação: {resposta}");
                    _clienteServidor.Close();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gateway] Erro ao ligar ao Servidor: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Desliga do Servidor enviando GW_DISCONNECT.
        /// </summary>
        private void DesligarDoServidor()
        {
            try
            {
                if (_clienteServidor != null && _clienteServidor.Connected)
                {
                    NetworkStream stream = _clienteServidor.GetStream();
                    StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };
                    writer.WriteLine($"GW_DISCONNECT {_gatewayId}");
                    Console.WriteLine($"[Gateway] Enviado: GW_DISCONNECT {_gatewayId}");
                    _clienteServidor.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gateway] Erro ao desligar do Servidor: {ex.Message}");
            }
        }

        /// <summary>
        /// Lê input do utilizador para comandos (ex: "sair").
        /// </summary>
        private void LerInput()
        {
            while (_running)
            {
                string? input = Console.ReadLine();
                if (input != null && input.Trim().ToLower() == "sair")
                {
                    Console.WriteLine("[Gateway] A encerrar...");
                    _running = false;
                    _listenerSensores?.Stop();
                    break;
                }
            }
        }

        /// <summary>
        /// Trata a comunicação com um sensor individual (executa numa thread separada).
        /// Primeiro executa o handshake com timeout, depois inicia a sessão normal.
        /// </summary>
        private void TratarSensor(TcpClient client)
        {
            string endpoint = client.Client.RemoteEndPoint?.ToString() ?? "desconhecido";

            try
            {
                // Fase 1: Handshake com timeout de 10 segundos
                bool handshakeOk = HandshakeHandler.ExecutarHandshake(client, out string? sensorId);

                if (!handshakeOk)
                {
                    return;
                }

                Console.WriteLine($"[Gateway] Handshake OK — sensor {sensorId} em sessão");

                // Fase 2: Sessão normal (sem timeout)
                SensorSession.IniciarSessao(client, sensorId!);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gateway] Erro com sensor {endpoint}: {ex.Message}");
            }
            finally
            {
                client.Close();
                Console.WriteLine($"[Gateway] Ligação encerrada: {endpoint}");
            }
        }
    }
}
