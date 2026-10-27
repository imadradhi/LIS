using System;
using System.Collections.Generic;

namespace Lis.Core.ASTM
{
    public abstract class AstmRecord
    {
        public abstract char RecordType { get; }
    }

    public class AstmHeaderRecord : AstmRecord
    {
        public override char RecordType => 'H';
        public string Delimiters { get; set; } = @"\^&";
        public string SenderId { get; set; } = "LIS";
        public string ReceiverId { get; set; } = string.Empty;
        public string ProcessingId { get; set; } = "P";
        public string VersionNo { get; set; } = "1";
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string ToAstmString()
        {
            return $"H|{Delimiters}|||{SenderId}||||||{ProcessingId}|{VersionNo}|{Timestamp:yyyyMMddHHmmss}";
        }
    }

    public class AstmPatientRecord : AstmRecord
    {
        public override char RecordType => 'P';
        public int SequenceNumber { get; set; } = 1;
        public string PracticeAssignedId { get; set; } = string.Empty;
        public string LaboratoryAssignedId { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public string Sex { get; set; } = "U";

        public string ToAstmString()
        {
            string dobStr = DateOfBirth.HasValue ? DateOfBirth.Value.ToString("yyyyMMdd") : "";
            return $"P|{SequenceNumber}|{PracticeAssignedId}|{LaboratoryAssignedId}||{PatientName}||{dobStr}|{Sex}";
        }
    }

    public class AstmOrderRecord : AstmRecord
    {
        public override char RecordType => 'O';
        public int SequenceNumber { get; set; } = 1;
        public string SpecimenId { get; set; } = string.Empty; // Accession number
        public string InstrumentSpecimenId { get; set; } = string.Empty;
        public List<string> UniversalTestCodes { get; set; } = new List<string>();
        public string Priority { get; set; } = "R"; // R = Routine, S = Stat
        public string SpecimenType { get; set; } = "Serum";
        public string ActionCode { get; set; } = "Q"; // Q = In response to Query, N = New, C = Cancel
        public string ReportType { get; set; } = "O";

        /// <summary>
        /// Extracts the specimen accession number from either SpecimenId or InstrumentSpecimenId,
        /// handling ASTM subfield delimiters (^) and space-padded identifiers (e.g. Sysmex ^^                000001^M).
        /// </summary>
        public string ExtractAccessionNo()
        {
            string candidate1 = ExtractFromField(SpecimenId);
            if (!string.IsNullOrWhiteSpace(candidate1)) return candidate1;

            string candidate2 = ExtractFromField(InstrumentSpecimenId);
            return candidate2;
        }

        private static string ExtractFromField(string? field)
        {
            if (string.IsNullOrWhiteSpace(field)) return string.Empty;

            var parts = field.Split('^');
            string best = string.Empty;

            foreach (var p in parts)
            {
                var trimmed = p.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // If it's a single letter mode flag like 'M' or 'A', keep looking unless nothing else found
                if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
                {
                    if (string.IsNullOrEmpty(best)) best = trimmed;
                    continue;
                }

                // If candidate has digits or is longer, prioritize it
                if (string.IsNullOrEmpty(best) || trimmed.Length > best.Length)
                {
                    best = trimmed;
                }
            }

            return best;
        }

        public string ToAstmString()
        {
            // Test codes format: ^^^CODE1\^^^CODE2
            var formattedTests = string.Join("\\", UniversalTestCodes);
            return $"O|{SequenceNumber}|{SpecimenId}|{InstrumentSpecimenId}|{formattedTests}|{Priority}||||||A||||{SpecimenType}||||||||||{ReportType}";
        }
    }

    public class AstmResultRecord : AstmRecord
    {
        public override char RecordType => 'R';
        public int SequenceNumber { get; set; } = 1;
        public string UniversalTestCode { get; set; } = string.Empty; // e.g. ^^^WBC or WBC
        public string Value { get; set; } = string.Empty;
        public string Units { get; set; } = string.Empty;
        public string ReferenceRanges { get; set; } = string.Empty;
        public string ResultAbnormalFlag { get; set; } = "N"; // N, H, L, HH, LL, A
        public string NatureOfAbnormality { get; set; } = string.Empty;
        public string ResultStatus { get; set; } = "F"; // F = Final, C = Corrected, P = Preliminary
        public DateTime? ResultDateTime { get; set; }
        public string InstrumentId { get; set; } = string.Empty;

        public static AstmResultRecord Parse(string[] fields)
        {
            var res = new AstmResultRecord();
            if (fields.Length > 1 && int.TryParse(fields[1], out var seq)) res.SequenceNumber = seq;
            if (fields.Length > 2) res.UniversalTestCode = ExtractTestCode(fields[2]);
            if (fields.Length > 3) res.Value = fields[3]?.Trim() ?? "";
            if (fields.Length > 4) res.Units = fields[4]?.Trim() ?? "";
            if (fields.Length > 5) res.ReferenceRanges = fields[5]?.Trim() ?? "";
            if (fields.Length > 6) res.ResultAbnormalFlag = fields[6]?.Trim() ?? "N";
            if (fields.Length > 8) res.ResultStatus = fields[8]?.Trim() ?? "F";
            if (fields.Length > 13) res.InstrumentId = fields[13]?.Trim() ?? "";

            return res;
        }

        private static string ExtractTestCode(string field)
        {
            if (string.IsNullOrWhiteSpace(field)) return "";
            // Often formatted as: ^^^CODE or ^^^CODE^NAME
            var parts = field.Split('^');
            foreach (var p in parts)
            {
                if (!string.IsNullOrWhiteSpace(p)) return p.Trim();
            }
            return field.Trim();
        }
    }

    public class AstmQueryRecord : AstmRecord
    {
        public override char RecordType => 'Q';
        public int SequenceNumber { get; set; } = 1;
        public string StartingRangeId { get; set; } = string.Empty; // Accession queried, e.g. ^000013
        public string EndingRangeId { get; set; } = string.Empty;
        public string UniversalTestId { get; set; } = "ALL";
        public string RequestInformationStatus { get; set; } = "O"; // O = Requesting test orders

        public string ExtractAccessionNo()
        {
            if (string.IsNullOrWhiteSpace(StartingRangeId)) return "";
            var parts = StartingRangeId.Split('^');
            foreach (var p in parts)
            {
                if (!string.IsNullOrWhiteSpace(p)) return p.Trim();
            }
            return StartingRangeId.Trim();
        }

        public static AstmQueryRecord Parse(string[] fields)
        {
            var q = new AstmQueryRecord();
            if (fields.Length > 1 && int.TryParse(fields[1], out var seq)) q.SequenceNumber = seq;
            if (fields.Length > 2) q.StartingRangeId = fields[2]?.Trim() ?? "";
            if (fields.Length > 3) q.EndingRangeId = fields[3]?.Trim() ?? "";
            if (fields.Length > 4) q.UniversalTestId = fields[4]?.Trim() ?? "ALL";
            if (fields.Length > 12) q.RequestInformationStatus = fields[12]?.Trim() ?? "O";

            return q;
        }
    }

    public class AstmCommentRecord : AstmRecord
    {
        public override char RecordType => 'C';
        public int SequenceNumber { get; set; } = 1;
        public string CommentSource { get; set; } = "I";
        public string Text { get; set; } = string.Empty;
        public string CommentType { get; set; } = "G";

        public static AstmCommentRecord Parse(string[] fields)
        {
            var c = new AstmCommentRecord();
            if (fields.Length > 1 && int.TryParse(fields[1], out var seq)) c.SequenceNumber = seq;
            if (fields.Length > 2) c.CommentSource = fields[2]?.Trim() ?? "I";
            if (fields.Length > 3) c.Text = fields[3]?.Trim() ?? "";
            if (fields.Length > 4) c.CommentType = fields[4]?.Trim() ?? "G";
            return c;
        }
    }

    public class AstmTerminatorRecord : AstmRecord
    {
        public override char RecordType => 'L';
        public int SequenceNumber { get; set; } = 1;
        public string TerminationCode { get; set; } = "N"; // N = Normal, Q = Error

        public string ToAstmString()
        {
            return $"L|{SequenceNumber}|{TerminationCode}";
        }
    }
}
