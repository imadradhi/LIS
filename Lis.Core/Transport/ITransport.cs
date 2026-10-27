using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lis.Core.Transport
{
    public interface ITransport : IDisposable
    {
        bool IsConnected { get; }
        string Description { get; }

        event EventHandler<byte[]>? DataReceived;
        event EventHandler<bool>? ConnectionStateChanged;
        event EventHandler<Exception>? ErrorOccurred;

        Task StartAsync(CancellationToken cancellationToken = default);
        Task StopAsync();
        Task SendAsync(byte[] data, CancellationToken cancellationToken = default);
        Task SendByteAsync(byte b, CancellationToken cancellationToken = default);
    }
}
