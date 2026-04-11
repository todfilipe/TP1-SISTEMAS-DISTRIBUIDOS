using System;
using System.Collections.Generic;
using System.Threading;

namespace Gateway
{
    /// <summary>
    /// Buffer local FIFO para mensagens que falharam no envio GW→Servidor.
    /// Capacidade máxima de 1000 medições. Retentativa com backoff exponencial
    /// (início 5s, máximo 60s), reset do backoff após envio bem-sucedido.
    /// Quando o buffer está cheio, descarta a mensagem mais antiga (FIFO).
    /// </summary>
    class RetryBuffer
    {
        private const int MaxCapacity = 1000;
        private const int InitialRetryMs = 5000;   // 5 segundos
        private const int MaxRetryMs = 60000;       // 60 segundos

        private readonly Queue<string> _queue = new Queue<string>();
        private readonly object _lock = new object();
        private readonly Func<string, string> _sendToServer;
        private Thread _retryThread;
        private volatile bool _running;
        private int _currentRetryMs = InitialRetryMs;

        public RetryBuffer(Func<string, string> sendToServer)
        {
            _sendToServer = sendToServer ?? throw new ArgumentNullException(nameof(sendToServer));
        }

        /// <summary>
        /// Adiciona uma mensagem ao buffer. Se o buffer estiver cheio (1000),
        /// descarta a mensagem mais antiga (FIFO) para abrir espaço.
        /// </summary>
        public void Enqueue(string message)
        {
            lock (_lock)
            {
                if (_queue.Count >= MaxCapacity)
                {
                    string discarded = _queue.Dequeue();
                    Console.WriteLine($"[BUFFER] Capacidade máxima ({MaxCapacity}) atingida — descartada mensagem mais antiga: {discarded}");
                }

                _queue.Enqueue(message);
                Console.WriteLine($"[BUFFER] Mensagem adicionada ao buffer ({_queue.Count}/{MaxCapacity}): {message}");
            }
        }

        /// <summary>Número atual de mensagens no buffer.</summary>
        public int Count
        {
            get { lock (_lock) { return _queue.Count; } }
        }

        /// <summary>Inicia a thread de retentativa em background.</summary>
        public void Start()
        {
            _running = true;
            _retryThread = new Thread(RetryLoop)
            {
                IsBackground = true,
                Name = "RetryBuffer"
            };
            _retryThread.Start();
            Console.WriteLine("[BUFFER] Thread de retentativa iniciada.");
        }

        /// <summary>Para a thread de retentativa.</summary>
        public void Stop()
        {
            _running = false;
            Console.WriteLine("[BUFFER] Thread de retentativa parada.");
        }

        private void RetryLoop()
        {
            while (_running)
            {
                string message = null;

                lock (_lock)
                {
                    if (_queue.Count > 0)
                        message = _queue.Peek(); // Espreitar sem remover
                }

                if (message == null)
                {
                    // Buffer vazio — esperar no intervalo mínimo e resetar backoff
                    _currentRetryMs = InitialRetryMs;
                    Thread.Sleep(InitialRetryMs);
                    continue;
                }

                // Tentar reenviar
                try
                {
                    Console.WriteLine($"[BUFFER] A reenviar mensagem para o Servidor: {message}");
                    string response = _sendToServer(message);

                    if (response != null && response.StartsWith("OK"))
                    {
                        // Sucesso — remover do buffer e resetar backoff
                        lock (_lock) { _queue.Dequeue(); }
                        _currentRetryMs = InitialRetryMs;
                        Console.WriteLine($"[BUFFER] Mensagem reenviada com sucesso. Restam {Count} no buffer.");

                        // Tentar enviar a próxima imediatamente (sem esperar)
                        continue;
                    }
                    else
                    {
                        Console.WriteLine($"[BUFFER] Retentativa falhou (resposta: {response}). Próxima tentativa em {_currentRetryMs / 1000}s.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BUFFER] Erro na retentativa: {ex.Message}. Próxima tentativa em {_currentRetryMs / 1000}s.");
                }

                // Esperar com backoff exponencial
                Thread.Sleep(_currentRetryMs);
                _currentRetryMs = Math.Min(_currentRetryMs * 2, MaxRetryMs);
            }
        }
    }
}
