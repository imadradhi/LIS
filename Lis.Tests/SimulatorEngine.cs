using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Lis.Core.ASTM;

namespace Lis.Tests
{
    public class SimulatorEngine : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private TcpClient? _client;
        private NetworkStream? _stream;

        public List<string> ReceivedWorklistFrames { get; } = new List<string>();

        public SimulatorEngine(string host = "127.0.0.1", int port = 5100)
        {
            _host = host;
            _port = port;
        }

        public async Task ConnectAsync()
        {
            _client = new TcpClient();
            await _client.ConnectAsync(_host, _port);
            _stream = _client.GetStream();
        }

        public async Task<List<string>> QueryWorklistAsync(string accessionNo)
        {
            if (_stream == null) throw new InvalidOperationException("Not connected to Middleware.");

            ReceivedWorklistFrames.Clear();

            // 1. Send ENQ
            await _stream.WriteAsync(new byte[] { AstmConstants.ENQ }, 0, 1);

            // Wait for ACK
            byte[] resp = new byte[1];
            await _stream.ReadAsync(resp, 0, 1);
            if (resp[0] != AstmConstants.ACK)
                throw new InvalidOperationException($"Expected ACK from Middleware, received: {resp[0]}");

            // 2. Build Query frames
            var frames = new List<string>
            {
                AstmFraming.BuildFrame(1, new AstmHeaderRecord { SenderId = "ADVIA360" }.ToAstmString()),
                AstmFraming.BuildFrame(2, $"Q|1|^{accessionNo}||ALL||||||||O"),
                AstmFraming.BuildFrame(3, new AstmTerminatorRecord().ToAstmString())
            };

            // 3. Send frames
            foreach (var frame in frames)
            {
                byte[] frameBytes = Encoding.ASCII.GetBytes(frame);
                await _stream.WriteAsync(frameBytes, 0, frameBytes.Length);

                // Wait for ACK
                await _stream.ReadAsync(resp, 0, 1);
                if (resp[0] != AstmConstants.ACK)
                    throw new InvalidOperationException($"Expected ACK from Middleware for frame, received: {resp[0]}");
            }

            // 4. Send EOT
            await _stream.WriteAsync(new byte[] { AstmConstants.EOT }, 0, 1);

            // 5. Read Worklist reply from Middleware
            await ReadWorklistReplyAsync();

            return ReceivedWorklistFrames;
        }

        private async Task ReadWorklistReplyAsync()
        {
            if (_stream == null) return;

            byte[] buffer = new byte[1024];
            var frameSb = new StringBuilder();

            while (true)
            {
                int read = await _stream.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0) break;

                for (int i = 0; i < read; i++)
                {
                    byte b = buffer[i];

                    if (b == AstmConstants.ENQ)
                    {
                        // Reply with ACK
                        await _stream.WriteAsync(new byte[] { AstmConstants.ACK }, 0, 1);
                    }
                    else if (b == AstmConstants.EOT)
                    {
                        return; // Done
                    }
                    else
                    {
                        frameSb.Append((char)b);
                        if (b == '\n' && frameSb.ToString().EndsWith("\r\n"))
                        {
                            string frameStr = frameSb.ToString();
                            frameSb.Clear();

                            if (AstmFraming.TryParseFrame(frameStr, out var frame) && frame != null)
                            {
                                ReceivedWorklistFrames.Add(frame.Content);
                                // Send ACK
                                await _stream.WriteAsync(new byte[] { AstmConstants.ACK }, 0, 1);
                            }
                        }
                    }
                }
            }
        }

        public async Task SendResultsAsync(string accessionNo, Dictionary<string, string> results, string instrumentCode = "ADVIA360")
        {
            if (_stream == null) throw new InvalidOperationException("Not connected to Middleware.");

            // 1. Send ENQ
            await _stream.WriteAsync(new byte[] { AstmConstants.ENQ }, 0, 1);

            byte[] resp = new byte[1];
            await _stream.ReadAsync(resp, 0, 1);
            if (resp[0] != AstmConstants.ACK)
                throw new InvalidOperationException($"Expected ACK from Middleware, received: {resp[0]}");

            // 2. Build Result Frames
            var frames = new List<string>();
            int fn = 1;

            frames.Add(AstmFraming.BuildFrame(fn++, new AstmHeaderRecord { SenderId = instrumentCode }.ToAstmString()));
            frames.Add(AstmFraming.BuildFrame(fn++, new AstmPatientRecord { SequenceNumber = 1 }.ToAstmString()));
            frames.Add(AstmFraming.BuildFrame(fn++, new AstmOrderRecord { SequenceNumber = 1, SpecimenId = accessionNo }.ToAstmString()));

            int rSeq = 1;
            foreach (var kvp in results)
            {
                string testCode = kvp.Key;
                string value = kvp.Value;
                frames.Add(AstmFraming.BuildFrame(fn++, $"R|{rSeq++}|^^^{testCode}|{value}|||N||F||||{DateTime.Now:yyyyMMddHHmmss}|{instrumentCode}"));
            }

            frames.Add(AstmFraming.BuildFrame(fn++, new AstmTerminatorRecord().ToAstmString()));

            // 3. Send frames
            foreach (var frame in frames)
            {
                byte[] frameBytes = Encoding.ASCII.GetBytes(frame);
                await _stream.WriteAsync(frameBytes, 0, frameBytes.Length);

                await _stream.ReadAsync(resp, 0, 1);
                if (resp[0] != AstmConstants.ACK)
                    throw new InvalidOperationException($"Expected ACK from Middleware for frame, received: {resp[0]}");
            }

            // 4. Send EOT
            await _stream.WriteAsync(new byte[] { AstmConstants.EOT }, 0, 1);
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _client?.Dispose();
        }
    }
}

