using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

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
        static TcpClient serverClient;
        static StreamReader serverReader;
        static StreamWriter serverWriter;
        static bool isRunning = true;

        // Gestor da configuração dos sensores (ficheiro sensors.csv)
        static SensorConfigManager configManager;

        // Objeto de sincronização para garantir exclusão mútua na comunicação com o Servidor
        static readonly object serverLock = new object();

        static void Main(string[] args)
        {
            // Validação dos parâmetros de arranque
            if (args.Length < 1)
            {
                Console.WriteLine("Uso: Gateway <gateway_id> [server_ip] [server_port]");
                return;
            }

            gatewayId = args[0];
            string serverIp = args.Length >= 2 ? args[1] : "127.0.0.1";
            int serverPort = args.Length >= 3 && int.TryParse(args[2], out int p) ? p : 9090;

            Console.WriteLine($"[GATEWAY] A iniciar com ID: {gatewayId}");

            // 1. Ligar ao Servidor
            try
            {
                serverClient = new TcpClient(serverIp, serverPort);
                var stream = serverClient.GetStream();

                // Inicializar leitores/escritores (UTF-8) com AutoFlush no StreamWriter
                serverReader = new StreamReader(stream, Encoding.UTF8);
                serverWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

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
                string csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensors.csv");
                configManager = new SensorConfigManager(csvPath);
                int loaded = configManager.LoadConfig();
                Console.WriteLine($"[GATEWAY] Configuração de sensores carregada ({loaded} sensor(es)).");
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

            // 3. Abrir Listener para os Sensores na porta 8080
            TcpListener sensorListener = null;
            try
            {
                sensorListener = new TcpListener(IPAddress.Any, 8080);
                sensorListener.Start();
                Console.WriteLine("[GATEWAY] A escutar Sensores na porta 8080...");

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
                serverClient?.Close();
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
                try
                {
                    serverWriter.WriteLine(message);
                    string response = serverReader.ReadLine();
                    return response;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO] Falha na comunicação com o Servidor: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Trata a comunicação com um Sensor de forma independente (numa thread separada).
        /// </summary>
        static void HandleSensor(TcpClient sensorClient)
        {
            string currentSensorId = "UNKNOWN";
            SensorState state = SensorState.AGUARDA_CONNECT;

            try
            {
                using (var stream = sensorClient.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
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
                                if (sensorCfg != null)
                                {
                                    // Verificar se o sensor está num estado que permite conexão
                                    if (sensorCfg.Estado == "desativado" || sensorCfg.Estado == "manutencao")
                                    {
                                        Console.WriteLine($"[SENSOR '{currentSensorId}'] rejeitado — estado actual: {sensorCfg.Estado}.");
                                        writer.WriteLine($"ERR_SENSOR_STATE {sensorCfg.Estado}");
                                        return;
                                    }
                                    Console.WriteLine($"[CONFIG] Sensor '{currentSensorId}' validado (zona: {sensorCfg.Zona}, estado: {sensorCfg.Estado}).");
                                }
                                else
                                {
                                    Console.WriteLine($"[CONFIG] AVISO: Sensor '{currentSensorId}' não existe no CSV — a aceitar sem validação.");
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

                                state = SensorState.OPERACIONAL;
                                Console.WriteLine($"[SENSOR '{currentSensorId}'] registou tipos: {parts[1]}");
                                writer.WriteLine("OK_TYPES_REGISTERED");
                                break;

                            case "DATA":
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
                                    DataValidator.ValidateAndProcessData(line, currentSensorId, configManager);

                                // Registar o resultado da validação na consola
                                Console.WriteLine(validationResult.LogMessage);

                                if (!validationResult.IsValid)
                                {
                                    // Enviar o código de erro específico ao sensor
                                    writer.WriteLine(validationResult.ErrorCode);
                                    break;
                                }

                                // ── Encaminhar para o Servidor ──
                                // A mensagem FORWARD já foi construída pelo validador
                                string serverResponse = SendToServer(validationResult.ForwardMessage);

                                if (serverResponse != null && serverResponse.StartsWith("OK"))
                                {
                                    writer.WriteLine("OK");
                                }
                                else
                                {
                                    Console.WriteLine($"[ERRO] Servidor respondeu: {serverResponse}");
                                    writer.WriteLine("ERR_SERVER");
                                }
                                break;

                            case "HEARTBEAT":
                                // HEARTBEAT <sensor_id> — stub para Fase 3
                                if (state != SensorState.OPERACIONAL)
                                {
                                    writer.WriteLine("ERR_SEQUENCE");
                                    break;
                                }
                                Console.WriteLine($"[SENSOR '{currentSensorId}'] heartbeat recebido.");
                                writer.WriteLine("OK");
                                break;

                            case "DISCONNECT":
                                // DISCONNECT <sensor_id>
                                if (parts.Length < 2)
                                {
                                    writer.WriteLine("ERR_INVALID_DATA");
                                    break;
                                }

                                string disconnectId = parts[1];
                                Console.WriteLine($"[SENSOR '{disconnectId}'] solicitou desconexão.");
                                writer.WriteLine("OK_DISCONNECT");

                                // Atualizar o estado do sensor na configuração CSV para 'desligado'
                                configManager?.ChangeSensorStatus(disconnectId, "desligado");

                                // Notificar o Servidor da mudança de estado (sd_rel.pdf secção 6.4)
                                string statusResponse = SendToServer($"SENSOR_STATUS {disconnectId} desligado");
                                if (statusResponse != null)
                                {
                                    Console.WriteLine($"[GATEWAY] SENSOR_STATUS enviado ao Servidor para '{disconnectId}'. Resposta: {statusResponse}");
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
                sensorClient.Close();
                Console.WriteLine($"[GATEWAY] Atendimento ao SENSOR '{currentSensorId}' finalizado.");
            }
        }
    }
}
