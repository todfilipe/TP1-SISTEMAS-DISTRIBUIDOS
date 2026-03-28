using System;
using System.IO;

namespace Servidor
{
    /// <summary>
    /// Entry point do Servidor.
    /// Inicia o servidor TCP na porta 9090 (ou porta passada como argumento).
    /// Uso: dotnet run [porta]
    /// Exemplo: dotnet run 9090
    /// Teste: dotnet run -- test
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            // Modo de teste: dotnet run -- test  OU  dotnet run -- --test
            if (args.Length >= 1 && (args[0] == "--test" || args[0] == "test"))
            {
                ExecutarTeste();
                return;
            }

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

        /// <summary>
        /// Teste de integração do DataStore: simula mensagens FORWARD e verifica CSVs gerados.
        /// </summary>
        static void ExecutarTeste()
        {
            Console.WriteLine("═══════════════════════════════════════════");
            Console.WriteLine("  TESTE DE ARMAZENAMENTO CSV - DataStore  ");
            Console.WriteLine("═══════════════════════════════════════════\n");

            string testDir = Path.Combine(Path.GetTempPath(), "datastore_test_" + Guid.NewGuid().ToString("N")[..8]);
            DataStore store = new DataStore(testDir);

            Console.WriteLine($"[Teste] Diretório de teste: {testDir}\n");

            var medicoes = new (string sensorId, string tipo, string valor, string zona, string timestamp)[]
            {
                ("S101", "TEMP",  "22.5",  "ZONA_CENTRO",      "2026-03-10T09:15:00"),
                ("S102", "TEMP",  "19.3",  "ZONA_ESCOLAR",     "2026-03-10T09:16:00"),
                ("S103", "TEMP",  "-2.1",  "ZONA_INDUSTRIAL",  "2026-03-10T09:17:00"),
                ("S201", "HUM",   "65.0",  "ZONA_RESIDENCIAL", "2026-03-10T09:15:00"),
                ("S301", "AR",    "42.0",  "ZONA_PARQUE",      "2026-03-10T09:15:00"),
                ("S401", "RUIDO", "78.5",  "ZONA_CENTRO",      "2026-03-10T09:15:00"),
                ("S501", "PM2.5", "12.3",  "ZONA_ESCOLAR",     "2026-03-10T09:15:00"),
                ("S502", "PM2.5", "8.7",   "ZONA_INDUSTRIAL",  "2026-03-10T09:16:00"),
                ("S601", "PM10",  "35.0",  "ZONA_RESIDENCIAL", "2026-03-10T09:15:00"),
                ("S701", "LUZ",   "850.0", "ZONA_PARQUE",      "2026-03-10T09:15:00"),
            };

            int sucesso = 0, falha = 0;
            foreach (var m in medicoes)
            {
                ResultadoArmazenamento resultado = store.ArmazenarMedicao(m.sensorId, m.tipo, m.valor, m.zona, m.timestamp);
                if (resultado == ResultadoArmazenamento.Sucesso)
                {
                    sucesso++;
                }
                else
                {
                    falha++;
                    Console.WriteLine($"  [FALHA] {m.sensorId} {m.tipo} {m.valor} -> {resultado}");
                }
            }

            Console.WriteLine("\n--- Testes de validação (devem ser rejeitados) ---");

            var invalidos = new (string sensorId, string tipo, string valor, string zona, string timestamp, string descricao)[]
            {
                ("S999", "INVALIDO", "10",   "ZONA_CENTRO",  "2026-03-10T09:15:00", "Tipo inválido"),
                ("S999", "TEMP",     "abc",  "ZONA_CENTRO",  "2026-03-10T09:15:00", "Valor não numérico"),
                ("S999", "HUM",      "-5.0", "ZONA_CENTRO",  "2026-03-10T09:15:00", "Negativo para HUM"),
                ("S999", "TEMP",     "20.0", "ZONA_INVALIDA","2026-03-10T09:15:00", "Zona inválida"),
                ("S999", "TEMP",     "20.0", "ZONA_CENTRO",  "timestamp_invalido",  "Timestamp inválido"),
            };

            int rejeicoes = 0;
            foreach (var m in invalidos)
            {
                ResultadoArmazenamento resultado = store.ArmazenarMedicao(m.sensorId, m.tipo, m.valor, m.zona, m.timestamp);
                if (resultado != ResultadoArmazenamento.Sucesso)
                {
                    rejeicoes++;
                    Console.WriteLine($"  [OK] {m.descricao} -> {resultado}");
                }
                else
                {
                    Console.WriteLine($"  [INESPERADO] {m.descricao} -> foi aceite mas devia ser rejeitado!");
                }
            }

            Console.WriteLine("\n═══════════════════════════════════════════");
            Console.WriteLine("  CONTEÚDO DOS FICHEIROS CSV GERADOS");
            Console.WriteLine("═══════════════════════════════════════════\n");

            string[] ficheiros = Directory.GetFiles(testDir, "*.csv");
            Array.Sort(ficheiros);
            foreach (string f in ficheiros)
            {
                Console.WriteLine($"--- {Path.GetFileName(f)} ---");
                Console.WriteLine(File.ReadAllText(f));
            }

            Console.WriteLine("═══════════════════════════════════════════");
            Console.WriteLine($"  Medições armazenadas: {sucesso}/{medicoes.Length}");
            Console.WriteLine($"  Dados inválidos rejeitados: {rejeicoes}/{invalidos.Length}");
            Console.WriteLine($"  Ficheiros CSV criados: {ficheiros.Length}");
            Console.WriteLine("═══════════════════════════════════════════");

            bool todosSucessos = sucesso == medicoes.Length && rejeicoes == invalidos.Length;
            Console.WriteLine(todosSucessos ? "\n  TODOS OS TESTES PASSARAM!\n" : "\n  ALGUNS TESTES FALHARAM!\n");

            try { Directory.Delete(testDir, true); } catch { }
        }
    }
}
