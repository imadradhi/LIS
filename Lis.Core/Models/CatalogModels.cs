using System;

namespace Lis.Core.Models
{
    public class Department
    {
        public long Id { get; set; }
        public string NameEn { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public char? Letter { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class SpecimenType
    {
        public long Id { get; set; }
        public string NameEn { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public string? Color { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class TestItem
    {
        public long Id { get; set; }
        public string TestCode { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public long? DepartmentId { get; set; }
        public long? SpecimenTypeId { get; set; }
        public bool IsProfile { get; set; }
        public decimal Price { get; set; }
        public int SortOrder { get; set; }
        public bool Active { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ComponentItem
    {
        public long Id { get; set; }
        public long TestId { get; set; }
        public string ComponentCode { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public string? DisplayCode { get; set; }
        public string? Unit { get; set; }
        public short? Decimals { get; set; }
        public string ResultType { get; set; } = "numeric";
        public string? Notes { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ReferenceRangeItem
    {
        public long Id { get; set; }
        public string? RangeCode { get; set; }
        public long ComponentId { get; set; }
        public string Sex { get; set; } = "B"; // 'M', 'F', 'B'
        public decimal AgeMin { get; set; }
        public decimal? AgeMax { get; set; }
        public string AgeUnit { get; set; } = "Years"; // 'Days', 'Months', 'Years'
        public int AgeFromDays { get; set; }
        public int? AgeToDays { get; set; }
        public decimal? Low { get; set; }
        public decimal? High { get; set; }
        public decimal? CriticalLow { get; set; }
        public decimal? CriticalHigh { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class InstrumentItem
    {
        public long Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Protocol { get; set; } // 'ASTM', 'HL7'
        public string? ConnectionType { get; set; } // 'TCP', 'RS232'
        public string? OperatingMode { get; set; } // 'Bidirectional', 'ResultsOnly'
        public string? TcpMode { get; set; } // 'Server', 'Client'
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? ComPort { get; set; }
        public int? BaudRate { get; set; }
        public int? DataBits { get; set; }
        public string? Parity { get; set; }
        public string? StopBits { get; set; }
        public string? FlowControl { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class InstrumentTestMapping
    {
        public long Id { get; set; }
        public long InstrumentId { get; set; }
        public long TestId { get; set; }
        public long ComponentId { get; set; }
        public string InstrumentTestCode { get; set; } = string.Empty;
        public decimal? ReportableLow { get; set; }
        public decimal? ReportableHigh { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
