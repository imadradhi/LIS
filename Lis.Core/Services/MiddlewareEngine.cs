using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Data;
using Lis.Core.Drivers;
using Lis.Core.Models;
using Lis.Core.Transport;

namespace Lis.Core.Services
{
    public class MiddlewareEngine : IDisposable
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IReferenceRangeService _refRangeService;
        private readonly List<IInstrumentDriver> _drivers = new();
        private CancellationTokenSource? _cts;

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
        public int ActiveDriversCount => _drivers.Count;

        public event EventHandler<bool>? StateChanged;
        public event EventHandler<(string instCode, string status)>? InstrumentStatusChanged;

        public MiddlewareEngine(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
            _refRangeService = new ReferenceRangeService(connectionFactory);
        }

        public async Task StartAsync()
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _drivers.Clear();

            try
            {
                using var conn = _connectionFactory.CreateConnection();
                var instruments = (await conn.QueryAsync<InstrumentItem>(@"
                    SELECT id, code, name, protocol, connection_type AS ConnectionType,
                           operating_mode AS OperatingMode, tcp_mode AS TcpMode, host, port,
                           com_port AS ComPort, baud_rate AS BaudRate, data_bits AS DataBits,
                           parity, stop_bits AS StopBits, flow_control AS FlowControl, active
                    FROM instruments
                    WHERE active = TRUE;
                ")).AsList();

                foreach (var inst in instruments)
                {
                    try
                    {
                        var transport = TransportFactory.CreateTransport(inst);
                        var driver = new AstmInstrumentDriver(inst, transport, _connectionFactory, _refRangeService);

                        driver.StatusChanged += (s, status) =>
                        {
                            InstrumentStatusChanged?.Invoke(this, (inst.Code, status));
                        };

                        _drivers.Add(driver);
                        await driver.StartAsync(_cts.Token);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MiddlewareEngine] Failed to start driver for {inst.Code}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MiddlewareEngine] Failed to load instruments: {ex.Message}");
            }

            StateChanged?.Invoke(this, true);
        }

        public async Task StopAsync()
        {
            if (!IsRunning) return;

            try
            {
                _cts?.Cancel();

                foreach (var driver in _drivers)
                {
                    try
                    {
                        await driver.StopAsync();
                        driver.Dispose();
                    }
                    catch { }
                }

                _drivers.Clear();
                _cts = null;
            }
            finally
            {
                StateChanged?.Invoke(this, false);
            }
        }

        public async Task RestartAsync()
        {
            await StopAsync();
            await Task.Delay(500);
            await StartAsync();
        }

        public void Dispose()
        {
            _ = StopAsync();
        }
    }
}
