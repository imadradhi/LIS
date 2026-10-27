using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Lis.Core.Transport
{
    public enum TcpMode
    {
        Server,
        Client
    }

    public class TcpTransport : ITransport
    {
        private readonly TcpMode _mode;
        private readonly string _host;
        private readonly int _port;

        private TcpListener? _listener;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private Task? _workerTask;
        private readonly object _sendLock = new object();
        private bool _isConnected;

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    ConnectionStateChanged?.Invoke(this, _isConnected);
                }
            }
        }

        public string Description => $"{_mode} ({_host}:{_port})";

        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler<bool>? ConnectionStateChanged;
        public event EventHandler<Exception>? ErrorOccurred;

        public TcpTransport(TcpMode mode, string host, int port)
        {
            _mode = mode;
            _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
            _port = port;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            _cts = new CancellationTokenSource();
            _workerTask = Task.Run(() => WorkerLoopAsync(_cts.Token), _cts.Token);
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            CloseClient();

            if (_listener != null)
            {
                try { _listener.Stop(); } catch { }
                _listener = null;
            }

            if (_workerTask != null)
            {
                try { await _workerTask; } catch { }
                _workerTask = null;
            }

            IsConnected = false;
        }

        private async Task WorkerLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_mode == TcpMode.Server)
                    {
                        if (_listener == null)
                        {
                            var ip = IPAddress.Any;
                            if (IPAddress.TryParse(_host, out var parsedIp)) ip = parsedIp;
                            _listener = new TcpListener(ip, _port);
                            _listener.Start();
                        }

                        // Wait for instrument connection
                        var client = await _listener.AcceptTcpClientAsync(ct);
                        _client = client;
                        _stream = client.GetStream();
                        IsConnected = true;

                        await ReadStreamLoopAsync(_stream, ct);
                    }
                    else // Client mode
                    {
                        _client = new TcpClient();
                        await _client.ConnectAsync(_host, _port, ct);
                        _stream = _client.GetStream();
                        IsConnected = true;

                        await ReadStreamLoopAsync(_stream, ct);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, ex);
                }
                finally
                {
                    CloseClient();
                    IsConnected = false;
                }

                // Auto-reconnect delay for client mode or brief pause on server error
                if (!ct.IsCancellationRequested)
                {
                    try { await Task.Delay(_mode == TcpMode.Client ? 3000 : 500, ct); } catch { }
                }
            }
        }

        private async Task ReadStreamLoopAsync(NetworkStream stream, CancellationToken ct)
        {
            byte[] buffer = new byte[4096];
            while (!ct.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead == 0) break; // Disconnected

                byte[] received = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, received, 0, bytesRead);
                DataReceived?.Invoke(this, received);
            }
        }

        public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (!IsConnected || _stream == null)
                throw new InvalidOperationException("Transport is not connected.");

            await _stream.WriteAsync(data, 0, data.Length, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }

        public async Task SendByteAsync(byte b, CancellationToken cancellationToken = default)
        {
            await SendAsync(new byte[] { b }, cancellationToken);
        }

        private void CloseClient()
        {
            if (_stream != null)
            {
                try { _stream.Dispose(); } catch { }
                _stream = null;
            }

            if (_client != null)
            {
                try { _client.Dispose(); } catch { }
                _client = null;
            }
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }
    }
}
