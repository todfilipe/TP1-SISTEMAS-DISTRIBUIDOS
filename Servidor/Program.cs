using System;

namespace Servidor
{
    /// <summary>
    /// Entry point do Servidor.
    /// Inicia o servidor TCP na porta 9090 (ou porta passada como argumento).
    /// Uso: dotnet run [porta]
    /// Exemplo: dotnet run 9090
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            int porta = 9090; // Porta por defeito conforme protocolo

            // Aceitar porta como argumento opcional
            if (args.Length >= 1)
            {
                if (int.TryParse(args[0], out int portaArg) && portaArg > 0 && portaArg <= 65535)
                {
                    porta = portaArg;
                }
                else
                {
                    Console.WriteLine($"Porta inválida: {args[0]}. A usar porta por defeito ({porta}).");
                }
            }

            Console.WriteLine($"[Servidor] A iniciar na porta {porta}...");
            Console.WriteLine("[Servidor] Escreve 'sair' para encerrar.\n");

            ServidorTCP servidor = new ServidorTCP(porta);
            servidor.Iniciar();
        }
    }
}
