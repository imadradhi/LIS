using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace Lis.Core.Transport
{
    public class SerialTransport : ITransport
    {
        private readonly string _portName;
        private readonly int _baudRate;
        private readonly int _dataBits;
        private readonly Parity _parity;
        private readonly StopBits _stopBits;
        private readonly Handshake _handshake;

        private SerialPort? _serialPort;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
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

        public string Description => $"RS232 ({_portName}, {_baudRate}bps)";

        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler<bool>? ConnectionStateChanged;
        public event EventHandler<Exception>? ErrorOccurred;

        public SerialTransport(
            string portName,
            int baudRate = 9600,
            int dataBits = 8,
            Parity parity = Parity.None,
            StopBits stopBits = StopBits.One,
            Handshake handshake = Handshake.None)
        {
            _portName = portName;
            _baudRate = baudRate;
            _dataBits = dataBits;
            _parity = parity;
            _stopBits = stopBits;
            _handshake = handshake;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            _cts = new CancellationTokenSource();
            _readTask = Task.Run(() => WorkerLoopAsync(_cts.Token), _cts.Token);
            return Task.CompletedTask;
        }

        private async Task WorkerLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _serialPort = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits)
                    {
                        Handshake = _handshake,
                        ReadTimeout = 2000,
                        WriteTimeout = 2000,
                        DtrEnable = true,
                        RtsEnable = true
                    };

                    _serialPort.Open();
                    IsConnected = true;

                    byte[] buffer = new byte[1024];
                    while (!ct.IsCancellationRequested && _serialPort.IsOpen)
                    {
                        try
                        {
                            int bytesRead = _serialPort.Read(buffer, 0, buffer.Length);
                            if (bytesRead > 0)
                            {
                                byte[] received = new byte[bytesRead];
                                Buffer.BlockCopy(buffer, 0, received, 0, bytesRead);
                                DataReceived?.Invoke(this, received);
                            }
                        }
                        catch (TimeoutException)
                        {
                            // Timeout is normal in serial reading, continue
                        }
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
                    ClosePort();
                    IsConnected = false;
                }

                if (!ct.IsCancellationRequested)
                {
                    try { await Task.Delay(3000, ct); } catch { }
                }
            }
        }

        public Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                throw new InvalidOperationException("Serial port is not open.");

            _serialPort.Write(data, 0, data.Length);
            return Task.CompletedTask;
        }

        public Task SendByteAsync(byte b, CancellationToken cancellationToken = default)
        {
            return SendAsync(new byte[] { b }, cancellationToken);
        }

        public async Task StopAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            ClosePort();

            if (_readTask != null)
            {
                try { await _readTask; } catch { }
                _readTask = null;
            }

            IsConnected = false;
        }

        private void ClosePort()
        {
            if (_serialPort != null)
            {
                try
                {
                    if (_serialPort.IsOpen) _serialPort.Close();
                    _serialPort.Dispose();
                }
                catch { }
                _serialPort = null;
            }
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }
    }
}
