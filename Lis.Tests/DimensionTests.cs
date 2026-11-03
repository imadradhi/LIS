using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lis.Core.Drivers;

namespace Lis.Tests
{
    public class DimensionTests
    {
        public Task Test_DimensionProtocol_Checksum_And_Framing()
        {
            // Verify Poll checksum calculation: P<FS><FS>1<FS>1<FS>0<FS> -> 6E
            string pollPayload = $"P{(char)DimensionProtocol.FS}{(char)DimensionProtocol.FS}1{(char)DimensionProtocol.FS}1{(char)DimensionProtocol.FS}0{(char)DimensionProtocol.FS}";
            string chk = DimensionProtocol.CalculateChecksum(pollPayload);
            Assert.Equal("6E", chk);

            // Verify No-Request packet: <STX>N<FS>6A<ETX>
            byte[] noReq = DimensionProtocol.BuildNoRequest();
            Assert.Equal(6, noReq.Length);
            Assert.Equal(DimensionProtocol.STX, noReq[0]);
            Assert.Equal((byte)'N', noReq[1]);
            Assert.Equal(DimensionProtocol.FS, noReq[2]);
            Assert.Equal((byte)'6', noReq[3]);
            Assert.Equal((byte)'A', noReq[4]);
            Assert.Equal(DimensionProtocol.ETX, noReq[5]);

            // Verify Result Acceptance packet: <STX>M<FS>A<FS><FS>E2<ETX>
            byte[] accept = DimensionProtocol.BuildAcceptance();
            Assert.Equal(9, accept.Length);
            Assert.Equal(DimensionProtocol.STX, accept[0]);
            Assert.Equal((byte)'M', accept[1]);
            Assert.Equal(DimensionProtocol.FS, accept[2]);
            Assert.Equal((byte)'A', accept[3]);
            Assert.Equal(DimensionProtocol.FS, accept[4]);
            Assert.Equal(DimensionProtocol.FS, accept[5]);
            Assert.Equal((byte)'E', accept[6]);
            Assert.Equal((byte)'2', accept[7]);
            Assert.Equal(DimensionProtocol.ETX, accept[8]);

            return Task.CompletedTask;
        }

        public Task Test_DimensionProtocol_ParseResultRecord()
        {
            string raw = "\x02R\x1c0\x1c279-38-000\x1c043092005\x1c1\x1c\x1c0\x1c174513190302\x1c1\x1c1\x1c2\x1cGLU\x1c85.00\x1cmg/dL\x1c\x1cBUN\x1c7\x1cmg/dL\x1c0C\x03";
            bool ok = DimensionProtocol.TryParseDimensionResult(raw, out var record);

            Assert.True(ok);
            Assert.NotNull(record);
            Assert.Equal("043092005", record!.AccessionNo);
            Assert.Equal("279-38-000", record.PatientId);
            Assert.Equal(2, record.Results.Count);

            Assert.Equal("GLU", record.Results[0].TestCode);
            Assert.Equal("85.00", record.Results[0].Value);
            Assert.Equal("mg/dL", record.Results[0].Units);

            Assert.Equal("BUN", record.Results[1].TestCode);
            Assert.Equal("7", record.Results[1].Value);
            Assert.Equal("mg/dL", record.Results[1].Units);

            return Task.CompletedTask;
        }

        public Task Test_DimensionProtocol_ToReadableString()
        {
            string raw = "\x02P\x1c\x1c1\x1c1\x1c0\x1c6E\x03";
            string readable = DimensionProtocol.ToReadableString(raw);
            Assert.Equal("<STX>P<FS><FS>1<FS>1<FS>0<FS>6E<ETX>", readable);

            return Task.CompletedTask;
        }

        public Task Test_DimensionProtocol_SampleRequest_ExactLegacyVector()
        {
            // Vector from Legacy Report Section 14:
            // <STX>D<FS>0<FS>0<FS>A<FS>12345<FS>1001<FS>1<FS><FS>0<FS>1<FS>**<FS>1<FS>2<FS>GLU<FS>CREA<FS>96<ETX>
            byte[] dPacket = DimensionProtocol.BuildSampleRequest("1001", "12345", new List<string> { "GLU", "CREA" });
            string readable = DimensionProtocol.ToReadableString(System.Text.Encoding.ASCII.GetString(dPacket));
            Assert.Equal("<STX>D<FS>0<FS>0<FS>A<FS>12345<FS>1001<FS>1<FS><FS>0<FS>1<FS>**<FS>1<FS>2<FS>GLU<FS>CREA<FS>96<ETX>", readable);

            // Verify checksum validator
            string frameStr = System.Text.Encoding.ASCII.GetString(dPacket);
            Assert.True(DimensionProtocol.ValidateChecksum(frameStr));

            return Task.CompletedTask;
        }
    }
}
