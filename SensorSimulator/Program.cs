using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;

namespace SensorSimulator
{
    class Program
    {
        static string gatewayIp = "127.0.0.1";
        static int gatewayPort1 = 8080;
        static int gatewayPort2 = 8090;
        
        // Número de sensores concorrentes a simular (teste de escalabilidade)
        static int numSensors = 200; 
        
        // Tempo entre os envios contínuos de DATA de cada sensor
        static int delayBetweenSendsMs = 1500; 

        static void Main(string[] args)
        {
            Console.WriteLine("=== SIMULADOR DE ESCALABILIDADE (MÚLTIPLOS SENSORES) ===\n");

            // 1. Preparar o ficheiro sensors.csv do Gateway
            string csvPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "Gateway", "sensors.csv"));
            
            // Failsafe path for compiled exe
            if (!File.Exists(csvPath)) 
                csvPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Gateway", "sensors.csv"));

            if (File.Exists(csvPath))
            {
                Console.WriteLine("[PREPARAÇÃO] A verificar os registos (sensors.csv) no Gateway...");
                var lines = new List<string>(File.ReadAllLines(csvPath));
                bool modified = false;

                for (int i = 1; i <= numSensors; i++)
                {
                    string sensorId = $"SIM{i:D3}"; // SIM001, SIM002...
                    if (!lines.Exists(l => l.StartsWith(sensorId + ":")))
                    {
                        lines.Add($"{sensorId}:ativo:ZONA_CENTRO:[TEMP]:2026-01-01T00:00:00");
                        modified = true;
                    }
                }

                if (modified)
                {
                    File.WriteAllLines(csvPath, lines);
                    Console.WriteLine($"[PREPARAÇÃO] Injetados {numSensors} sensores simulados (SIM001 a SIM{numSensors:D3}) no ficheiro.");
                    Console.WriteLine("!!! IMPORTANTE: Se o Gateway já estiver a correr, terás de o REINICIAR agora para ele carregar os novos sensores SIM001..SIM050 !!!\n");
                    Console.WriteLine("Pressione ENTER para continuar após garantir que o Gateway está pronto...");
                    Console.ReadLine();
                }
            }
            else
            {
                Console.WriteLine($"[AVISO] Ficheiro sensors.csv não encontrado no caminho: {csvPath}");
            }

            Console.WriteLine($"\n[ARRANQUE] A iniciar {numSensors} threads de sensores concorrentes (metade para a porta {gatewayPort1} e metade para a porta {gatewayPort2})...\n");

            // 2. Iniciar N Threads, cada uma simula um Sensor isolado
            List<Task> tasks = new List<Task>();
            for (int i = 1; i <= numSensors; i++)
            {
                int id = i;
                int portaAlvo = (i % 2 == 0) ? gatewayPort2 : gatewayPort1;
                tasks.Add(Task.Run(() => SimulateSensor($"SIM{id:D3}", portaAlvo)));
                Thread.Sleep(20); // Ligeiro desfasamento no arranque para não entupir a porta de vez
            }

            // Bloqueia a thread principal para os sensores correrem indefinidamente
            Task.WaitAll(tasks.ToArray());
            Console.WriteLine("Todos os sensores terminaram.");
        }

        static void SimulateSensor(string sensorId, int portaAlvo)
        {
            try
            {
                using TcpClient client = new TcpClient();
                client.Connect(gatewayIp, portaAlvo);
                
                using NetworkStream stream = client.GetStream();
                using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                using StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };

                // Handshake - Fase 1: CONNECT
                writer.WriteLine($"CONNECT {sensorId}");
                string resp = reader.ReadLine();
                if (resp != $"OK_CONNECTED {sensorId}")
                {
                    Console.WriteLine($"[{sensorId}] Fechado - Erro no CONNECT: {resp}");
                    return;
                }

                // Handshake - Fase 2: REGISTER_TYPES
                writer.WriteLine("REGISTER_TYPES TEMP");
                resp = reader.ReadLine();
                if (resp != "OK_TYPES_REGISTERED")
                {
                    Console.WriteLine($"[{sensorId}] Fechado - Erro no REGISTER_TYPES: {resp}");
                    return;
                }

                Console.WriteLine($"[{sensorId}] Ligado ao Gateway na porta {portaAlvo}! A iniciar envio de dados.");

                Random rnd = new Random();
                int msgCount = 0;

                // Loop Principal - Simular medições com ciclo FOR eterno
                while (true)
                {
                    string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"); // ISO 8601 exigido
                    double temp = 20.0 + (rnd.NextDouble() * 10.0);
                    
                    string tempStr = temp.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    writer.WriteLine($"DATA TEMP {tempStr} ZONA_CENTRO {timestamp}");
                    resp = reader.ReadLine();
                    
                    if (resp == "OK")
                    {
                        msgCount++;
                        // Registo a cada 20 mensagens enviadas para não superlotar o terminal local
                        if (msgCount % 20 == 0) 
                        {
                            Console.WriteLine($"[{sensorId}] Já enviou {msgCount} mensagens estabilizadas para o Gateway.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[{sensorId}] Gateway devolveu Erro: {resp}");
                    }
                    
                    // Espera X ms (mais um ligeiro random jitter para evitar colisão absoluta de threads)
                    Thread.Sleep(delayBetweenSendsMs + rnd.Next(-100, 100)); 
                }
            }
            catch (Exception ex)
            {
                // Caiu por socket ou timeout
                Console.WriteLine($"[{sensorId}] Conexão caiu: {ex.Message}");
            }
        }
    }
}
