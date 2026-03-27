using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Gateway
{
    /// <summary>
    /// Responsável pela fase de handshake com timeout de 10 segundos.
    /// Sequência obrigatória:
    ///   1. CONNECT <sensor_id>     → OK_CONNECTED <sensor_id>
    ///   2. REGISTER_TYPES <tipos>  → OK_TYPES_REGISTERED
    /// Se a sequência não for completada em 10s, a ligação é fechada.
    /// </summary>
    public static class HandshakeHandler
    {
        private const int HANDSHAKE_TIMEOUT_MS = 10_000; // 10 segundos

        /// <summary>
        /// Executa o handshake completo com timeout.
        /// </summary>
        /// <param name="client">TcpClient do sensor</param>
        /// <param name="sensorId">ID do sensor (preenchido se CONNECT for bem sucedido)</param>
        /// <returns>true se o handshake completou com sucesso, false caso contrário</returns>
        public static bool ExecutarHandshake(TcpClient client, out string? sensorId)
        {
            sensorId = null;
            string endpoint = client.Client.RemoteEndPoint?.ToString() ?? "desconhecido";

            try
            {
                // Definir timeout de 10 segundos para o handshake completo
                client.ReceiveTimeout = HANDSHAKE_TIMEOUT_MS;

                NetworkStream stream = client.GetStream();
                StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true, NewLine = "\n" };

                // --- Passo 1: Esperar CONNECT <sensor_id> ---
                string? linha1 = reader.ReadLine();

                if (linha1 == null)
                {
                    return false;
                }

                linha1 = linha1.Trim();

                if (!linha1.StartsWith("CONNECT "))
                {
                    writer.WriteLine("ERR_SEQUENCE");
                    return false;
                }

                // Extrair sensor_id
                sensorId = linha1.Substring("CONNECT ".Length).Trim();

                if (string.IsNullOrWhiteSpace(sensorId))
                {
                    writer.WriteLine("ERR_INVALID_DATA");
                    return false;
                }

                // Stub: aceitar qualquer sensor_id
                writer.WriteLine($"OK_CONNECTED {sensorId}");
                Console.WriteLine($"[Gateway] CONNECT {sensorId} → OK_CONNECTED {sensorId}");

                // --- Passo 2: Esperar REGISTER_TYPES <tipos> ---
                string? linha2 = reader.ReadLine();

                if (linha2 == null)
                {
                    return false;
                }

                linha2 = linha2.Trim();

                if (!linha2.StartsWith("REGISTER_TYPES "))
                {
                    writer.WriteLine("ERR_SEQUENCE");
                    return false;
                }

                string tipos = linha2.Substring("REGISTER_TYPES ".Length).Trim();

                // Stub: aceitar todos os tipos
                writer.WriteLine("OK_TYPES_REGISTERED");
                Console.WriteLine($"[Gateway] REGISTER_TYPES {tipos} → OK_TYPES_REGISTERED");

                // Handshake completo — remover timeout
                client.ReceiveTimeout = 0;

                return true;
            }
            catch (IOException ex) when (ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.TimedOut)
            {
                // Timeout de handshake — 10 segundos expirados
                string sensorLabel = sensorId ?? endpoint;
                string timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                Console.WriteLine($"[Gateway] HANDSHAKE_TIMEOUT sensor={sensorLabel} ts={timestamp} — ligação fechada após 10s sem completar CONNECT+REGISTER_TYPES");
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gateway] Erro no handshake: {ex.Message}");
                return false;
            }
        }
    }
}
