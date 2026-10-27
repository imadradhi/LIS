using System;
using System.Text;

namespace Lis.Core.ASTM
{
    public static class AstmConstants
    {
        public const byte STX = 0x02;
        public const byte ETX = 0x03;
        public const byte EOT = 0x04;
        public const byte ENQ = 0x05;
        public const byte ACK = 0x06;
        public const byte NAK = 0x15;
        public const byte ETB = 0x17;
        public const byte CR  = 0x0D;
        public const byte LF  = 0x0A;
    }

    public class AstmFrame
    {
        public int FrameNumber { get; set; }
        public string Content { get; set; } = string.Empty;
        public bool IsEndBlock { get; set; } = true; // true = ETX, false = ETB
        public bool IsChecksumValid { get; set; } = true;
    }

    public static class AstmFraming
    {
        public static string CalculateChecksum(string contentWithTerminator)
        {
            // Sum of all characters modulo 256
            int sum = 0;
            foreach (char c in contentWithTerminator)
            {
                sum += (byte)c;
            }

            int mod = sum % 256;
            return mod.ToString("X2");
        }

        public static string BuildFrame(int frameNumber, string text, bool isLast = true)
        {
            int fn = frameNumber % 8; // ASTM frames are 0 to 7
            char terminator = isLast ? (char)AstmConstants.ETX : (char)AstmConstants.ETB;
            string payload = $"{fn}{text}{terminator}";
            string checksum = CalculateChecksum(payload);

            return $"{(char)AstmConstants.STX}{payload}{checksum}\r\n";
        }

        public static bool TryParseFrame(string rawFrame, out AstmFrame? frame)
        {
            frame = null;
            if (string.IsNullOrEmpty(rawFrame)) return false;

            int stxIdx = rawFrame.IndexOf((char)AstmConstants.STX);
            if (stxIdx < 0) return false;

            int etxIdx = rawFrame.IndexOf((char)AstmConstants.ETX, stxIdx);
            bool isEndBlock = true;

            if (etxIdx < 0)
            {
                etxIdx = rawFrame.IndexOf((char)AstmConstants.ETB, stxIdx);
                isEndBlock = false;
            }

            if (etxIdx < 0 || etxIdx + 2 >= rawFrame.Length) return false;

            // Character right after STX is frame number (0-7)
            if (etxIdx <= stxIdx + 1) return false;

            char fnChar = rawFrame[stxIdx + 1];
            if (!int.TryParse(fnChar.ToString(), out int frameNum))
                return false;

            // Text between frame number and ETX/ETB
            string content = rawFrame.Substring(stxIdx + 2, etxIdx - (stxIdx + 2));

            // Checksum is 2 hex chars after ETX/ETB
            string receivedChecksum = rawFrame.Substring(etxIdx + 1, 2);

            // Compute expected checksum
            string checksumPayload = rawFrame.Substring(stxIdx + 1, etxIdx - (stxIdx + 1) + 1);
            string expectedChecksum = CalculateChecksum(checksumPayload);

            bool isValid = string.Equals(receivedChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase);

            frame = new AstmFrame
            {
                FrameNumber = frameNum,
                Content = content,
                IsEndBlock = isEndBlock,
                IsChecksumValid = isValid
            };

            return true;
        }
    }
}
