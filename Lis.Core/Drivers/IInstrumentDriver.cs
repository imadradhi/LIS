using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lis.Core.Drivers
{
    public interface IInstrumentDriver : IDisposable
    {
        string InstrumentCode { get; }
        bool IsConnected { get; }
        event EventHandler<string>? StatusChanged;
        Task StartAsync(CancellationToken cancellationToken = default);
        Task StopAsync();
    }
}
