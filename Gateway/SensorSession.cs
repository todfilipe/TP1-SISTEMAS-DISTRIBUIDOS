using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Gateway
{
    /// <summary>
    /// Trata a sessão de um sensor após o handshake ter sido concluído com sucesso.
    /// Loop de leitura para: DATA, HEARTBEAT, DISCONNECT.
    /// </summary>
    public static class SensorSession
    {
        /// <summary>
        /// Inicia a sessão normal com o sensor (após handshake OK).
        /// </summary>
        public static void IniciarSessao(TcpClient client, string sensorId)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };

                string? linha;
                while ((linha = reader.ReadLine()) != null)
                {
                    linha = linha.Trim();
                    if (string.IsNullOrEmpty(linha))
                        continue;

                    Console.WriteLine($"[<- {sensorId}] {linha}");

                    string resposta = ProcessarMensagem(linha, sensorId);

                    writer.WriteLine(resposta);
                    Console.WriteLine($"[-> {sensorId}] {resposta}");

                    if (linha.StartsWith("DISCONNECT"))
                    {
                        break;
                    }
                }
            }
            catch (IOException)
            {
                Console.WriteLine($"[Gateway] Sensor {sensorId} desligou-se abruptamente.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gateway] Erro na sessão com sensor {sensorId}: {ex.Message}");
            }
        }

        private static string ProcessarMensagem(string mensagem, string sensorId)
        {
            string[] partes = mensagem.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (partes.Length == 0)
                return "ERR_INVALID_DATA";

            string comando = partes[0];

            switch (comando)
            {
                case "DATA":
                    if (partes.Length != 5) return "ERR_INVALID_DATA";
                    return "OK";

                case "HEARTBEAT":
                    return "OK";

                case "DISCONNECT":
                    return "OK_DISCONNECT";

                default:
                    return "ERR_INVALID_DATA";
            }
        }
    }
}
