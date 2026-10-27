using System;
using System.Collections.Generic;
using System.Text;

namespace Lis.Core.ASTM
{
    public class ParsedAstmMessage
    {
        public AstmHeaderRecord? Header { get; set; }
        public AstmPatientRecord? Patient { get; set; }
        public AstmOrderRecord? Order { get; set; }
        public AstmQueryRecord? Query { get; set; }
        public List<AstmResultRecord> Results { get; } = new List<AstmResultRecord>();
        public List<AstmCommentRecord> Comments { get; } = new List<AstmCommentRecord>();
        public AstmTerminatorRecord? Terminator { get; set; }

        public bool IsQuery => Query != null;
        public bool HasResults => Results.Count > 0;
    }

    public static class AstmMessageParser
    {
        public static ParsedAstmMessage ParseMessage(string rawText)
        {
            var msg = new ParsedAstmMessage();
            if (string.IsNullOrWhiteSpace(rawText)) return msg;

            var lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                char recordType = trimmed[0];
                var fields = trimmed.Split('|');

                switch (recordType)
                {
                    case 'H':
                        msg.Header = new AstmHeaderRecord();
                        if (fields.Length > 4) msg.Header.SenderId = fields[4];
                        break;

                    case 'P':
                        msg.Patient = new AstmPatientRecord();
                        if (fields.Length > 1 && int.TryParse(fields[1], out var pSeq)) msg.Patient.SequenceNumber = pSeq;
                        if (fields.Length > 2) msg.Patient.PracticeAssignedId = fields[2];
                        if (fields.Length > 3) msg.Patient.LaboratoryAssignedId = fields[3];
                        if (fields.Length > 5) msg.Patient.PatientName = fields[5];
                        if (fields.Length > 8) msg.Patient.Sex = fields[8];
                        break;

                    case 'O':
                        msg.Order = new AstmOrderRecord();
                        if (fields.Length > 1 && int.TryParse(fields[1], out var oSeq)) msg.Order.SequenceNumber = oSeq;
                        if (fields.Length > 2) msg.Order.SpecimenId = fields[2];
                        if (fields.Length > 3) msg.Order.InstrumentSpecimenId = fields[3];
                        if (fields.Length > 4 && !string.IsNullOrEmpty(fields[4]))
                        {
                            var tests = fields[4].Split('\\');
                            foreach (var t in tests)
                            {
                                if (!string.IsNullOrWhiteSpace(t)) msg.Order.UniversalTestCodes.Add(t.Trim());
                            }
                        }
                        break;

                    case 'Q':
                        msg.Query = AstmQueryRecord.Parse(fields);
                        break;

                    case 'R':
                        msg.Results.Add(AstmResultRecord.Parse(fields));
                        break;

                    case 'C':
                        msg.Comments.Add(AstmCommentRecord.Parse(fields));
                        break;

                    case 'L':
                        msg.Terminator = new AstmTerminatorRecord();
                        if (fields.Length > 2) msg.Terminator.TerminationCode = fields[2];
                        break;
                }
            }

            return msg;
        }

        public static List<string> BuildWorklistFrames(string accessionNo, long mrn, string patientName, List<string> instrumentTestCodes, string specimenType = "EDTA Whole Blood")
        {
            var frames = new List<string>();
            int frameNum = 1;

            // 1. Header
            var header = new AstmHeaderRecord { SenderId = "LIS", Timestamp = DateTime.Now };
            frames.Add(AstmFraming.BuildFrame(frameNum++, header.ToAstmString()));

            // 2. Patient
            var patient = new AstmPatientRecord
            {
                SequenceNumber = 1,
                PracticeAssignedId = mrn.ToString(),
                PatientName = patientName
            };
            frames.Add(AstmFraming.BuildFrame(frameNum++, patient.ToAstmString()));

            // 3. Order (Worklist)
            var order = new AstmOrderRecord
            {
                SequenceNumber = 1,
                SpecimenId = accessionNo,
                SpecimenType = specimenType,
                ActionCode = "Q", // In reply to host query
                UniversalTestCodes = new List<string>()
            };

            foreach (var code in instrumentTestCodes)
            {
                order.UniversalTestCodes.Add($"^^^{code}");
            }

            frames.Add(AstmFraming.BuildFrame(frameNum++, order.ToAstmString()));

            // 4. Terminator
            var term = new AstmTerminatorRecord { SequenceNumber = 1, TerminationCode = "N" };
            frames.Add(AstmFraming.BuildFrame(frameNum++, term.ToAstmString()));

            return frames;
        }
    }
}
