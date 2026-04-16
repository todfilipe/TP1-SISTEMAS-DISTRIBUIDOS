namespace Sensor;

class Program
{
    static void Main(string[] args)
    {
        // Valores por defeito caso o utilizador só escreva "dotnet run"
        string gatewayIp = "127.0.0.1";
        int gatewayPort = 8080;

        // Se o utilizador escrever: dotnet run 127.0.0.1 8090
        if (args.Length >= 1) 
        {
            gatewayIp = args[0];
        }
        if (args.Length >= 2 && int.TryParse(args[1], out int p)) 
        {
            gatewayPort = p;
        }

        // Inicia a tua interface que já está perfeita!
        SensorCLI cli = new SensorCLI();
        
        // Passa o IP e a Porta que lemos do terminal
        cli.Run(gatewayIp, gatewayPort);
    }
}