using System;
using System.Collections.Generic;

namespace Lis.Core.Services
{
    public class SpecimenLabelData
    {
        public string AccessionNo { get; set; } = string.Empty; // Exactly 6 digits
        public long Mrn { get; set; }
        public string PatientNameAr { get; set; } = string.Empty;
        public DateTime DateOfBirth { get; set; }
        public string SpecimenTypeName { get; set; } = string.Empty;
        public string? DepartmentLetter { get; set; }
        public string? ColorHex { get; set; }

        public string Line1Left => $"{AccessionNo} - {DateOfBirth:dd/MM/yyyy}";
        public string Line1Right => $"P.ID :{Mrn}";
        
        /// <summary>
        /// Displays only the first and second names of the patient on the barcode sticker (e.g. "زينب حيدر" or "عبد الله محمد").
        /// </summary>
        public string Line2Left => GetFirstTwoNames(PatientNameAr);
        public string Line2Right => SpecimenTypeName;

        public static string GetFirstTwoNames(string? fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 2) return fullName.Trim();

            // Handle Arabic compound prefixes like "عبد الله", "أبو بكر", etc.
            var compoundPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "عبد", "أبو", "ابو", "أم", "ام", "ابن", "بنت"
            };

            int index = 0;
            string firstName = parts[index++];
            if (compoundPrefixes.Contains(firstName) && index < parts.Length)
            {
                firstName += " " + parts[index++];
            }

            if (index >= parts.Length) return firstName;

            string secondName = parts[index++];
            if (compoundPrefixes.Contains(secondName) && index < parts.Length)
            {
                secondName += " " + parts[index++];
            }

            return $"{firstName} {secondName}";
        }
    }
}
