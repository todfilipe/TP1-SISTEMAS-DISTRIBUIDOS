namespace Sensor;

class Program
{
    static void Main(string[] args)
    {
        // O IP do Gateway é recebido como argumento; por defeito usa localhost
        string gatewayHost = "127.0.0.1";
        int gatewayPort = 8080;
        int videoPort = 8081;

        if (args.Length >= 1)
            gatewayHost = args[0];

        if (args.Length >= 2 && int.TryParse(args[1], out int port))
            gatewayPort = port;

        if (args.Length >= 3 && int.TryParse(args[2], out int vp))
            videoPort = vp;

        Console.WriteLine($"Gateway: {gatewayHost}:{gatewayPort} (vídeo: {videoPort})");
        Console.WriteLine();

        var cli = new SensorCLI();
        cli.Run(gatewayHost, gatewayPort, videoPort);
    }
}
