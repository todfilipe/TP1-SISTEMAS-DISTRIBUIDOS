using System;
using System.Collections.Generic;
using System.Threading;

namespace OneHealthMonitor.Core
{
    /// <summary>
    /// Buffer local FIFO para mensagens que falharam no envio GW→Servidor.
    /// Capacidade máxima de 1000 medições. Retentativa com backoff exponencial.
    /// </summary>
    public class RetryBuffer
    {
        private const int MaxCapacity = 1000;
        private const int InitialRetryMs = 5000;
        private const int MaxRetryMs = 60000;

        private readonly Queue<string> _queue = new Queue<string>();
        private readonly object _lock = new object();
        private readonly Func<string, string?> _sendToServer;
        private Thread? _retryThread;
        private volatile bool _running;
        private int _currentRetryMs = InitialRetryMs;

        public event Action<string>? OnLogMessage;

        public RetryBuffer(Func<string, string?> sendToServer)
        {
            _sendToServer = sendToServer ?? throw new ArgumentNullException(nameof(sendToServer));
        }

        private void Log(string msg) => OnLogMessage?.Invoke(msg);

        public void Enqueue(string message)
        {
            lock (_lock)
            {
                if (_queue.Count >= MaxCapacity)
                {
                    string discarded = _queue.Dequeue();
                    Log($"[BUFFER] Capacidade máxima ({MaxCapacity}) atingida — descartada mensagem mais antiga.");
                }

                _queue.Enqueue(message);
                Log($"[BUFFER] Mensagem adicionada ao buffer ({_queue.Count}/{MaxCapacity}).");
            }
        }

        public int Count
        {
            get { lock (_lock) { return _queue.Count; } }
        }

        public int MaxCount => MaxCapacity;

        public int CurrentRetryMs => _currentRetryMs;

        public void Start()
        {
            _running = true;
            _retryThread = new Thread(RetryLoop)
            {
                IsBackground = true,
                Name = "RetryBuffer"
            };
            _retryThread.Start();
            Log("[BUFFER] Thread de retentativa iniciada.");
        }

        public void Stop()
        {
            _running = false;
            Log("[BUFFER] Thread de retentativa parada.");
        }

        public void Clear()
        {
            lock (_lock)
            {
                _queue.Clear();
                Log("[BUFFER] Buffer limpo.");
            }
        }

        private void RetryLoop()
        {
            while (_running)
            {
                string? message = null;

                lock (_lock)
                {
                    if (_queue.Count > 0)
                        message = _queue.Peek();
                }

                if (message == null)
                {
                    _currentRetryMs = InitialRetryMs;
                    Thread.Sleep(InitialRetryMs);
                    continue;
                }

                try
                {
                    Log($"[BUFFER] A reenviar mensagem para o Servidor...");
                    string? response = _sendToServer(message);

                    if (response != null && response.StartsWith("OK"))
                    {
                        lock (_lock) { _queue.Dequeue(); }
                        _currentRetryMs = InitialRetryMs;
                        Log($"[BUFFER] Mensagem reenviada com sucesso. Restam {Count} no buffer.");
                        continue;
                    }
                    else
                    {
                        Log($"[BUFFER] Retentativa falhou. Próxima em {_currentRetryMs / 1000}s.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[BUFFER] Erro na retentativa: {ex.Message}. Próxima em {_currentRetryMs / 1000}s.");
                }

                Thread.Sleep(_currentRetryMs);
                _currentRetryMs = Math.Min(_currentRetryMs * 2, MaxRetryMs);
            }
        }
    }
}
