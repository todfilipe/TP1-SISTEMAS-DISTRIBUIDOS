using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Gateway
{
    class Program
    {
        static string gatewayId;
        static TcpClient serverClient;
        static StreamReader serverReader;
        static StreamWriter serverWriter;
        static bool isRunning = true;

        // Objeto de sincronização para garantir exclusão mútua na escrita para o Servidor
        static readonly object serverLock = new object();

        static void Main(string[] args)
        {
            // Validação do parâmetro de arranque (ID do Gateway)
            if (args.Length < 1)
            {
                Console.WriteLine("Uso: Gateway <gateway_id>");
                return;
            }

            gatewayId = args[0];
            Console.WriteLine($"[GATEWAY] A iniciar com ID: {gatewayId}");

            // 1. Ligar ao Servidor (IP: 127.0.0.1, Porta: 9090)
            try
            {
                serverClient = new TcpClient("127.0.0.1", 9090);
                var stream = serverClient.GetStream();
                
                // Inicializar leitores/escritores (UTF-8) com AutoFlush no StreamWriter
                serverReader = new StreamReader(stream, Encoding.UTF8);
                serverWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                // Enviar a mensagem inicial de conexão ao Servidor
                serverWriter.WriteLine($"GW_CONNECT {gatewayId}");
                Console.WriteLine($"[GATEWAY] Pedido de ligação enviado ao Servidor (GW_CONNECT {gatewayId}).");

                // Aguardar a reposta de confirmação
                string response = serverReader.ReadLine();
                if (response != "OK_GW_CONNECTED")
                {
                    Console.WriteLine($"[ERRO] Falha ao ligar ao Servidor. Resposta obtida: {response}");
                    return;
                }

                Console.WriteLine("[GATEWAY] Ligado com sucesso ao Servidor (recebido OK_GW_CONNECTED).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Não foi possível ligar ao Servidor (127.0.0.1:9090): {ex.Message}");
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
                        // Marcar como background de facto evita impedir fechar o processo se a thread ficar presa
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
        /// Trata a comunicação com um Sensor de forma independente (numa thread separada).
        /// </summary>
        static void HandleSensor(TcpClient sensorClient)
        {
            string currentSensorId = "UNKNOWN"; // Variável local à thread para armazenar o ID do sensor atual

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
                                if (parts.Length >= 2)
                                {
                                    currentSensorId = parts[1];
                                    Console.WriteLine($"[SENSOR '{currentSensorId}'] conectou-se.");
                                    
                                    // Responder sucesso
                                    writer.WriteLine($"OK_CONNECTED {currentSensorId}");
                                }
                                break;

                            case "REGISTER_TYPES":
                                // REGISTER_TYPES <t1,t2,...>
                                Console.WriteLine($"[SENSOR '{currentSensorId}'] solicitou registo de tipos.");
                                
                                // O enunciado diz apenas para responder OK nesta fase
                                writer.WriteLine("OK_TYPES_REGISTERED");
                                break;

                            case "DATA":
                                // DATA <tipo> <valor> <zona> <timestamp>
                                if (parts.Length >= 5)
                                {
                                    string tipo = parts[1];
                                    string valor = parts[2];
                                    string zona = parts[3];
                                    string timestamp = parts[4];
                                    
                                    Console.WriteLine($"[SENSOR '{currentSensorId}'] enviou DATA: {tipo}={valor} na zona {zona}");

                                    // Encaminhar para o servidor usando FORWARD via Escritor global
                                    // Utilizamos Lock pois MÚLTIPLAS THREADS (sensores) podem tentar escrever no socket do Servidor em simultâneo
                                    lock (serverLock)
                                    {
                                        try
                                        {
                                            serverWriter.WriteLine($"FORWARD {currentSensorId} {tipo} {valor} {zona} {timestamp}");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"[ERRO] Falha ao enviar FORWARD ao Servidor: {ex.Message}");
                                        }
                                    }

                                    // Responder OK de recebimento ao sensor
                                    writer.WriteLine("OK");
                                }
                                break;

                            case "DISCONNECT":
                                // DISCONNECT <sensor_id>
                                if (parts.Length >= 2)
                                {
                                    string disconnectId = parts[1];
                                    Console.WriteLine($"[SENSOR '{disconnectId}'] solicitou desconexão.");
                                    
                                    writer.WriteLine("OK_DISCONNECT");
                                    
                                    // Sai da função e permite fechar as streams limpidamente (graceful exit)
                                    return;
                                }
                                break;

                            default:
                                Console.WriteLine($"[AVISO] Comando desconhecido de {currentSensorId}: {line}");
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Como pode acontecer quando forçado desconectar
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
