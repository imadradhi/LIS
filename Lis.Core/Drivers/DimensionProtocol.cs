using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lis.Core.Drivers
{
    public static class DimensionProtocol
    {
        public const byte STX = 0x02;
        public const byte ETX = 0x03;
        public const byte FS = 0x1C;
        public const byte ENQ = 0x05;
        public const byte ACK = 0x06;
        public const byte NAK = 0x15;
        public const byte EOT = 0x04;

        /// <summary>
        /// Calculates the 8-bit additive sum (modulo 256) of bytes formatted as a 2-character uppercase hex string.
        /// </summary>
        public static string CalculateChecksum(string payload)
        {
            int sum = 0;
            foreach (char c in payload)
            {
                sum += (byte)c;
            }
            return (sum % 256).ToString("X2");
        }

        /// <summary>
        /// Validates that the 2 characters before ETX match the sum of characters between STX and checksum.
        /// </summary>
        public static bool ValidateChecksum(string frame)
        {
            if (string.IsNullOrEmpty(frame) || frame.Length < 4) return false;
            int stxIdx = frame.IndexOf((char)STX);
            int etxIdx = frame.LastIndexOf((char)ETX);
            if (stxIdx < 0 || etxIdx <= stxIdx + 2) return false;

            string chkGiven = frame.Substring(etxIdx - 2, 2);
            string body = frame.Substring(stxIdx + 1, etxIdx - stxIdx - 3);
            string chkCalc = CalculateChecksum(body);
            return string.Equals(chkGiven, chkCalc, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds a standard Dimension No-Request packet: <STX>N<FS>6A<ETX>
        /// </summary>
        public static byte[] BuildNoRequest()
        {
            return new byte[] { STX, (byte)'N', FS, (byte)'6', (byte)'A', ETX };
        }

        /// <summary>
        /// Builds a standard Dimension Result Acceptance packet: <STX>M<FS>A<FS><FS>E2<ETX>
        /// </summary>
        public static byte[] BuildAcceptance()
        {
            return new byte[] { STX, (byte)'M', FS, (byte)'A', FS, FS, (byte)'E', (byte)'2', ETX };
        }

        /// <summary>
        /// Builds a Dimension Sample Request Message (<STX>D<FS>...) to download test orders to the analyzer.
        /// Verified exact wire format from working legacy middleware:
        /// <STX>D<FS>0<FS>0<FS>A<FS>[PatientID]<FS>[Barcode]<FS>1<FS><FS>0<FS>1<FS>**<FS>1<FS>[Count]<FS>[TEST1]<FS>[TEST2]...<FS>[CHK]<ETX>
        /// </summary>
        public static byte[] BuildSampleRequest(string accessionNo, string patientId, List<string> testCodes, bool isStat = false)
        {
            string priority = isStat ? "1" : "0";
            var sb = new StringBuilder();
            sb.Append('D').Append((char)FS);
            sb.Append('0').Append((char)FS); // carrier
            sb.Append('0').Append((char)FS); // loadlist
            sb.Append('A').Append((char)FS); // action (Add)
            
            // Patient ID (strictly ASCII alphanumeric, max 16 chars, no question marks or symbols)
            string safeId = "";
            if (!string.IsNullOrWhiteSpace(patientId))
            {
                var clean = patientId.Trim().Where(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '-' || c == '_').ToArray();
                safeId = new string(clean);
                if (safeId.Length > 16) safeId = safeId.Substring(0, 16);
            }
            sb.Append(safeId).Append((char)FS);
            sb.Append(accessionNo.Trim()).Append((char)FS);
            sb.Append('1').Append((char)FS); // sample type (1=serum)
            sb.Append((char)FS);             // location = empty (<FS><FS>)
            sb.Append(priority).Append((char)FS); // priority (0=routine, 1=STAT)
            sb.Append('1').Append((char)FS); // # of cups
            sb.Append("**").Append((char)FS); // cup position (**) for barcoded tubes
            sb.Append('1').Append((char)FS); // dilution
            sb.Append(testCodes.Count).Append((char)FS);

            foreach (var test in testCodes)
            {
                sb.Append(test.Trim().ToUpperInvariant()).Append((char)FS);
            }

            string payload = sb.ToString();
            string chk = CalculateChecksum(payload);
            string full = $"{(char)STX}{payload}{chk}{(char)ETX}";
            return Encoding.ASCII.GetBytes(full);
        }

        /// <summary>
        /// Converts raw bytes/string containing control characters to human-readable format for logging.
        /// </summary>
        public static string ToReadableString(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            return raw
                .Replace("\x02", "<STX>")
                .Replace("\x03", "<ETX>")
                .Replace("\x1c", "<FS>")
                .Replace("\x04", "<EOT>")
                .Replace("\x05", "<ENQ>")
                .Replace("\x06", "<ACK>");
        }

        /// <summary>
        /// Parses a Dimension Result record (<STX>R<FS>...) extracting accession number and assay results.
        /// </summary>
        public static bool TryParseDimensionResult(string rawMessage, out DimensionResultRecord? record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(rawMessage)) return false;

            string clean = rawMessage.Trim('\x02', '\x03');
            string[] fields = clean.Split((char)FS);
            if (fields.Length < 4) return false;

            string recordType = fields[0].Trim();
            if (!recordType.StartsWith("R", StringComparison.OrdinalIgnoreCase)) return false;

            var result = new DimensionResultRecord
            {
                RawMessage = rawMessage,
                PatientId = fields.Length > 2 ? fields[2].Trim() : string.Empty,
                AccessionNo = fields.Length > 3 ? fields[3].Trim() : string.Empty
            };

            if (string.IsNullOrWhiteSpace(result.AccessionNo))
            {
                result.AccessionNo = result.PatientId;
            }

            // Standard Dimension format: Field 10 is count of tests, followed by 4-field tuples
            if (fields.Length >= 14 && int.TryParse(fields[10].Trim(), out int testCount) && testCount > 0)
            {
                int idx = 11;
                for (int t = 0; t < testCount && idx + 1 < fields.Length; t++)
                {
                    string testCode = fields[idx].Trim();
                    string val = (idx + 1 < fields.Length) ? fields[idx + 1].Trim() : string.Empty;
                    string units = (idx + 2 < fields.Length) ? fields[idx + 2].Trim() : string.Empty;
                    string flag = (idx + 3 < fields.Length) ? fields[idx + 3].Trim() : string.Empty;

                    if (!string.IsNullOrWhiteSpace(testCode) && !string.IsNullOrWhiteSpace(val))
                    {
                        result.Results.Add(new DimensionTestResult
                        {
                            TestCode = testCode,
                            Value = val,
                            Units = units,
                            Flag = flag
                        });
                    }
                    idx += 4;
                }
            }

            // Fallback scanner: looks for any assay code followed by a numeric value
            if (result.Results.Count == 0)
            {
                for (int i = 4; i < fields.Length - 1; i++)
                {
                    string candidateCode = fields[i].Trim();
                    string candidateVal = fields[i + 1].Trim();

                    if (candidateCode.Length >= 2 && candidateCode.Length <= 8 &&
                        char.IsLetter(candidateCode[0]) &&
                        decimal.TryParse(candidateVal, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                    {
                        string units = (i + 2 < fields.Length && fields[i + 2].Length <= 10) ? fields[i + 2].Trim() : string.Empty;
                        string flag = (i + 3 < fields.Length && fields[i + 3].Length <= 5) ? fields[i + 3].Trim() : string.Empty;

                        result.Results.Add(new DimensionTestResult
                        {
                            TestCode = candidateCode,
                            Value = candidateVal,
                            Units = units,
                            Flag = flag
                        });
                        i += 2;
                    }
                }
            }

            record = result;
            return result.Results.Count > 0 || !string.IsNullOrWhiteSpace(result.AccessionNo);
        }
    }

    public class DimensionResultRecord
    {
        public string RawMessage { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;
        public string AccessionNo { get; set; } = string.Empty;
        public List<DimensionTestResult> Results { get; set; } = new();
    }

    public class DimensionTestResult
    {
        public string TestCode { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Units { get; set; } = string.Empty;
        public string Flag { get; set; } = string.Empty;
    }
}

