using System;
using System.IO.Ports;
using Lis.Core.Models;

namespace Lis.Core.Transport
{
    public static class TransportFactory
    {
        public static ITransport CreateTransport(InstrumentItem instrument)
        {
            if (string.Equals(instrument.ConnectionType, "RS232", StringComparison.OrdinalIgnoreCase))
            {
                string portName = instrument.ComPort ?? "COM1";
                int baudRate = instrument.BaudRate > 0 ? instrument.BaudRate.Value : 9600;
                int dataBits = instrument.DataBits > 0 ? instrument.DataBits.Value : 8;

                Parity parity = Parity.None;
                if (!string.IsNullOrWhiteSpace(instrument.Parity) && Enum.TryParse<Parity>(instrument.Parity, true, out var p))
                    parity = p;

                StopBits stopBits = StopBits.One;
                if (!string.IsNullOrWhiteSpace(instrument.StopBits) && Enum.TryParse<StopBits>(instrument.StopBits, true, out var sb))
                    stopBits = sb;

                Handshake handshake = Handshake.None;
                if (!string.IsNullOrWhiteSpace(instrument.FlowControl) && Enum.TryParse<Handshake>(instrument.FlowControl, true, out var hs))
                    handshake = hs;

                return new SerialTransport(portName, baudRate, dataBits, parity, stopBits, handshake);
            }
            else // Default to TCP/IP
            {
                TcpMode mode = string.Equals(instrument.TcpMode, "Client", StringComparison.OrdinalIgnoreCase) 
                    ? TcpMode.Client 
                    : TcpMode.Server;

                string host = string.IsNullOrWhiteSpace(instrument.Host) ? "127.0.0.1" : instrument.Host;
                int port = instrument.Port > 0 ? instrument.Port.Value : 5100;

                return new TcpTransport(mode, host, port);
            }
        }
    }
}
