using System;

namespace Gateway
{
    /// <summary>
    /// Entry point do Gateway.
    /// Uso: dotnet run <gateway_id>
    /// Exemplo: dotnet run GW01
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            string gatewayId = "GW01"; // ID por defeito

            if (args.Length >= 1 && !string.IsNullOrWhiteSpace(args[0]))
            {
                gatewayId = args[0];
            }

            Console.WriteLine($"[Gateway] A iniciar com ID: {gatewayId}");

            GatewayTCP gateway = new GatewayTCP(gatewayId);
            gateway.Iniciar();
        }
    }
}
