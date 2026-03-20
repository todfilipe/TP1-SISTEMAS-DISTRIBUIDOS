namespace Sensor;

class Program
{
    static void Main(string[] args)
    {
        // O IP do Gateway é recebido como argumento; por defeito usa localhost
        string gatewayHost = "127.0.0.1";
        int gatewayPort = 8080;

        if (args.Length >= 1)
            gatewayHost = args[0];

        if (args.Length >= 2 && int.TryParse(args[1], out int port))
            gatewayPort = port;

        Console.WriteLine($"Gateway: {gatewayHost}:{gatewayPort}");
        Console.WriteLine();

        var cli = new SensorCLI();
        cli.Run(gatewayHost, gatewayPort);
    }
}
