using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Gateway
{
    internal sealed class ReadingAggregator
    {
        private readonly ConcurrentQueue<string> _pendingReadings = new ConcurrentQueue<string>();
        private readonly Func<string, string> _sendToServer;
        private readonly RetryBuffer _retryBuffer;
        private Thread _thread = null!;
        private volatile bool _running;

        public ReadingAggregator(Func<string, string> sendToServer, RetryBuffer retryBuffer)
        {
            _sendToServer = sendToServer;
            _retryBuffer = retryBuffer;
        }

        public void Enqueue(string forwardMessage)
        {
            _pendingReadings.Enqueue(forwardMessage);
        }

        public void Start()
        {
            _running = true;
            _thread = new Thread(ProcessLoop)
            {
                IsBackground = true,
                Name = "ReadingAggregator"
            };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
        }

        private void ProcessLoop()
        {
            while (_running)
            {
                Thread.Sleep(15000);
                ProcessBatch();
            }
        }

        private void ProcessBatch()
        {
            var batch = new List<string>();
            while (_pendingReadings.TryDequeue(out string? forwardMsg))
            {
                batch.Add(forwardMsg);
            }

            if (batch.Count == 0)
            {
                return;
            }

            Console.WriteLine($"\n[AGREGADOR] A processar {batch.Count} mensagens recebidas nos últimos 15s...");
            var grouped = new Dictionary<string, List<double>>();

            foreach (string msg in batch)
            {
                string[] parts = msg.Split(' ');
                if (parts.Length < 6)
                {
                    Console.WriteLine($"[AGREGADOR] AVISO: mensagem descartada por formato inválido (esperados >=6 blocos, recebidos {parts.Length}). Mensagem raw: '{msg}'");
                    continue;
                }

                string type = parts[2];
                string valueText = parts[3];
                string zone = parts[4];

                if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    string key = $"{type}|{zone}";
                    if (!grouped.ContainsKey(key))
                    {
                        grouped[key] = new List<double>();
                    }
                    grouped[key].Add(value);
                }
            }

            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            foreach (var kvp in grouped)
            {
                string[] keyParts = kvp.Key.Split('|');
                string type = keyParts[0];
                string zone = keyParts[1];
                double average = kvp.Value.Average();
                string averageText = average.ToString("F2", CultureInfo.InvariantCulture);
                string packet = $"FORWARD_AGGREGATED {type} {averageText} {zone} {timestamp}";

                string response = _sendToServer(packet);
                if (response == null)
                {
                    Console.WriteLine($"[AVISO] Servidor Central falhou a ligação. Agregado protegido no buffer/disco: {packet}");
                    _retryBuffer.Enqueue(packet);
                }
                else if (!response.StartsWith("OK"))
                {
                    Console.WriteLine($"[Descartado] O Servidor Central vetou ativamente os dados agregados! (Resposta: {response})");
                }
                else
                {
                    Console.WriteLine($"[AGREGADOR] DB OK -> {packet}");
                }
            }
        }
    }
}
