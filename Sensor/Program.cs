using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace Sensor;

class Program
{
    static void Main(string[] args)
    {
        // Se o utilizador passar o argumento --auto, corre no modo automático parametrizado
        bool isCustomAuto = args.Length >= 2 && args[0] == "--auto";

        // Se o utilizador passar o argumento --cli ou passar argumentos de IP/porta, corre no modo interativo original
        if (!isCustomAuto && args.Length >= 1 && (args[0] == "--cli" || args.Length >= 2))
        {
            string gatewayIp = args.Length >= 1 && args[0] != "--cli" ? args[0] : "127.0.0.1";
            int gatewayPort = 8080;
            if (args.Length >= 2 && int.TryParse(args[1], out int p))
            {
                gatewayPort = p;
            }

            Console.WriteLine("[SENSOR] A iniciar no modo CLI interativo...");
            SensorCLI cli = new SensorCLI();
            cli.Run(gatewayIp, gatewayPort);
            return;
        }

        string rabbitHost = "localhost";
        int rabbitPort = 5672;
        string rabbitUser = "admin";
        string rabbitPass = "admin";
        string rabbitVHost = "onehealth";

        string sensorId = "S101";
        string zone = "ZONA_CENTRO";
        string type = "TEMP";
        int intervalSeconds = 5;

        if (isCustomAuto)
        {
            if (args.Length >= 2) sensorId = args[1];
            if (args.Length >= 3) zone = args[2];
            if (args.Length >= 4) type = args[3];
            if (args.Length >= 5 && int.TryParse(args[4], out int sec)) intervalSeconds = sec;

            // Tenta obter credenciais do RabbitMQ do appsettings.json se disponível
            try
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                var config = builder.Build();
                rabbitHost = config["RabbitMQ:Host"] ?? rabbitHost;
                if (int.TryParse(config["RabbitMQ:Port"], out int rp)) rabbitPort = rp;
                rabbitUser = config["RabbitMQ:UserName"] ?? rabbitUser;
                rabbitPass = config["RabbitMQ:Password"] ?? rabbitPass;
                rabbitVHost = config["RabbitMQ:VirtualHost"] ?? rabbitVHost;
            }
            catch {}
        }
        else
        {
            // Caso contrário, corre no modo automático contínuo com base no appsettings.json
            Console.WriteLine("[SENSOR] A carregar configuração a partir do appsettings.json...");

            IConfiguration config;
            try
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config = builder.Build();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO CONFIG] Falha ao ler appsettings.json: {ex.Message}");
                Console.WriteLine("A iniciar modo CLI de salvaguarda...");
                SensorCLI cli = new SensorCLI();
                cli.Run("127.0.0.1", 8080);
                return;
            }

            rabbitHost = config["RabbitMQ:Host"] ?? rabbitHost;
            rabbitPort = int.TryParse(config["RabbitMQ:Port"], out int rp) ? rp : rabbitPort;
            rabbitUser = config["RabbitMQ:UserName"] ?? rabbitUser;
            rabbitPass = config["RabbitMQ:Password"] ?? rabbitPass;
            rabbitVHost = config["RabbitMQ:VirtualHost"] ?? rabbitVHost;

            sensorId = config["Sensor:SensorId"] ?? sensorId;
            zone = config["Sensor:Zone"] ?? zone;
            type = config["Sensor:Type"] ?? type;
            intervalSeconds = int.TryParse(config["Sensor:IntervalSeconds"], out int sec) ? sec : intervalSeconds;
        }

        // Criar e iniciar o cliente automático
        using var client = new SensorClient(
            sensorId,
            rabbitHost,
            rabbitPort,
            zone,
            type,
            intervalSeconds,
            rabbitUser,
            rabbitPass,
            rabbitVHost
        );

        // Iniciar ligação inicial
        Console.WriteLine($"[SENSOR] A ligar ao RabbitMQ em {rabbitHost}:{rabbitPort}...");
        Console.WriteLine($"[SENSOR AUTOMÁTICO] ID: {sensorId}, Zona: {zone}, Tipo: {type}, Intervalo: {intervalSeconds}s");
        if (!client.EnsureConnection())
        {
            Console.WriteLine("[SENSOR] Não foi possível estabelecer a ligação inicial ao RabbitMQ. O mecanismo de reconexão continuará a tentar em background.");
        }

        // Iniciar publicação automática de leituras
        client.StartAutomaticPublishing();

        // Bloquear a thread principal até encerramento (Ctrl+C)
        var exitEvent = new ManualResetEvent(false);
        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine("\n[SENSOR] A encerrar o Sensor automático...");
            e.Cancel = true;
            client.SendDisconnect();
            exitEvent.Set();
        };

        exitEvent.WaitOne();
    }
}