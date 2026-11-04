using System;
using System.Text;

namespace Lis.Core.Utils
{
    public static class HexUtils
    {
        /// <summary>
        /// Converts a raw byte array to a space-delimited uppercase hex string: "02 31 48 7C 0D 0A"
        /// </summary>
        public static string ToHexString(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            return BitConverter.ToString(bytes).Replace("-", " ");
        }

        /// <summary>
        /// Converts single byte to two-character uppercase hex string: "05", "06", etc.
        /// </summary>
        public static string ToHexString(byte b)
        {
            return b.ToString("X2");
        }

        /// <summary>
        /// Converts an ASCII/text payload (supporting control replacements like &lt;STX&gt;, &lt;ACK&gt;) into space-delimited hex string.
        /// </summary>
        public static string ToHexRepresentation(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            string decoded = raw
                .Replace("<STX>", "\x02")
                .Replace("<ETX>", "\x03")
                .Replace("<EOT>", "\x04")
                .Replace("<ENQ>", "\x05")
                .Replace("<ACK>", "\x06")
                .Replace("<NAK>", "\x15")
                .Replace("<FS>", "\x1C")
                .Replace("<CR>", "\x0D")
                .Replace("<LF>", "\x0A");

            byte[] bytes = Encoding.ASCII.GetBytes(decoded);
            return ToHexString(bytes);
        }

        /// <summary>
        /// Formats text / hex into a classic multi-line hex dump with offsets, hex pairs, and printable ASCII column:
        /// 0000  02 31 48 7C 5C 5E 26 7C 7C 7C 0D 03 34 32 0D 0A  |.1H|\^&|||..42..|
        /// </summary>
        public static string ToFormattedHexDump(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            string decoded = raw
                .Replace("<STX>", "\x02")
                .Replace("<ETX>", "\x03")
                .Replace("<EOT>", "\x04")
                .Replace("<ENQ>", "\x05")
                .Replace("<ACK>", "\x06")
                .Replace("<NAK>", "\x15")
                .Replace("<FS>", "\x1C")
                .Replace("<CR>", "\x0D")
                .Replace("<LF>", "\x0A");

            byte[] bytes = Encoding.ASCII.GetBytes(decoded);
            var sbHex = new StringBuilder();
            var sbAscii = new StringBuilder();
            var sbResult = new StringBuilder();

            for (int i = 0; i < bytes.Length; i++)
            {
                sbHex.Append(bytes[i].ToString("X2")).Append(' ');
                char c = (char)bytes[i];
                sbAscii.Append(char.IsControl(c) ? '.' : c);

                if ((i + 1) % 16 == 0 || i == bytes.Length - 1)
                {
                    int pad = 16 - ((i % 16) + 1);
                    if (pad > 0 && i == bytes.Length - 1)
                    {
                        sbHex.Append(new string(' ', pad * 3));
                    }

                    int lineOffset = (i / 16) * 16;
                    sbResult.AppendLine($"{lineOffset:X4}  {sbHex} |{sbAscii}|");
                    sbHex.Clear();
                    sbAscii.Clear();
                }
            }

            return sbResult.ToString();
        }
    }
}

