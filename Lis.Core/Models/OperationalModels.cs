using System;

namespace Lis.Core.Models
{
    public class Patient
    {
        public long Id { get; set; }
        public long Mrn { get; set; }
        public string FullNameAr { get; set; } = string.Empty;
        public string? FullNameEn { get; set; }
        public string Sex { get; set; } = "M"; // 'M', 'F'
        public DateTime DateOfBirth { get; set; }
        public string? Phone { get; set; }
        public string? NationalId { get; set; }
        public string? Notes { get; set; }
        public long? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class Order
    {
        public long Id { get; set; }
        public long PatientId { get; set; }
        public int VisitNo { get; set; } = 1;
        public DateTime OrderDatetime { get; set; }
        public string? ReferringDoctor { get; set; }
        public string? ClinicalNotes { get; set; }
        public string Status { get; set; } = "Ordered";
        public long? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class Specimen
    {
        public long Id { get; set; }
        public long OrderId { get; set; }
        public long SpecimenTypeId { get; set; }
        public string AccessionNo { get; set; } = string.Empty;
        public string Status { get; set; } = "Ordered";
        public DateTime? CollectedAt { get; set; }
        public DateTime IssuedAt { get; set; }
        public bool Archived { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class OrderTest
    {
        public long Id { get; set; }
        public long OrderId { get; set; }
        public long SpecimenId { get; set; }
        public long TestId { get; set; }
        public string Status { get; set; } = "Ordered";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class Result
    {
        public long Id { get; set; }
        public long OrderTestId { get; set; }
        public long ComponentId { get; set; }
        public string? ValueText { get; set; }
        public decimal? ValueNumeric { get; set; }
        public string? Unit { get; set; }
        public string? Flag { get; set; } // 'N','H','L','HH','LL'
        public long? InstrumentId { get; set; }
        public DateTime? ResultedAt { get; set; }
        public long? ValidatedBy { get; set; }
        public DateTime? ValidatedAt { get; set; }
        public bool IsCurrent { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
