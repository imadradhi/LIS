using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Data;

namespace Lis.Core.Services
{
    public class LabSettingsDto
    {
        public long Id { get; set; } = 1;
        public string LabNameAr { get; set; } = "مختبر التحليلات الطبية التخصصي";
        public string LabNameEn { get; set; } = "Specialized Medical Laboratory";
        public byte[]? LogoData { get; set; }
        public string? Address { get; set; } = "العراق - بغداد";
        public string? Phones { get; set; } = "07700000000";
        public string? Email { get; set; } = "lab@example.com";
        public string? ReportHeaderText { get; set; } = "مختبر التحليلات المرضية المتقدمة";
        public string? ReportFooterText { get; set; } = "نتائج الفحوصات تعتمد سريرياً بالربط مع حالة المريض";
        public string? ManagerName { get; set; } = "د. أحمد علي";
        public string? ManagerTitle { get; set; } = "أخصائي التحليلات المرضية";
        public byte[]? SignatureData { get; set; }
        public string PaperSize { get; set; } = "A4";
        public string Currency { get; set; } = "IQD";
        public string? LabelPrinter { get; set; }
        public string? ReportPrinter { get; set; }
        public string? ReceiptPrinter { get; set; }
        public double BarcodeOffsetMm { get; set; } = 15.0;
        public string Theme { get; set; } = "Night";
    }

    public interface ILabSettingsService
    {
        Task<LabSettingsDto> GetSettingsAsync();
        Task SaveSettingsAsync(LabSettingsDto settings, long? currentUserId = null);
    }

    public class LabSettingsService : ILabSettingsService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IAuthService _authService;

        public LabSettingsService(IDbConnectionFactory connectionFactory, IAuthService authService)
        {
            _connectionFactory = connectionFactory;
            _authService = authService;
        }

        public async Task<LabSettingsDto> GetSettingsAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            
            // Ensure columns exist (automatic schema migration)
            await conn.ExecuteAsync(@"
                ALTER TABLE lab_settings ADD COLUMN IF NOT EXISTS barcode_offset_mm DOUBLE PRECISION DEFAULT 15.0;
                ALTER TABLE lab_settings ADD COLUMN IF NOT EXISTS theme TEXT DEFAULT 'Night';
                UPDATE lab_settings SET label_printer = 'HPRT SL32BT' WHERE label_printer IS NULL;
            ");

            const string sql = @"
                SELECT id, 
                       lab_name_ar AS LabNameAr, 
                       lab_name_en AS LabNameEn, 
                       logo_data AS LogoData, 
                       address, 
                       phones, 
                       email, 
                       report_header_text AS ReportHeaderText, 
                       report_footer_text AS ReportFooterText, 
                       manager_name AS ManagerName, 
                       manager_title AS ManagerTitle, 
                       signature_data AS SignatureData, 
                       paper_size AS PaperSize, 
                       currency, 
                       label_printer AS LabelPrinter, 
                       report_printer AS ReportPrinter, 
                       receipt_printer AS ReceiptPrinter,
                       COALESCE(barcode_offset_mm, 15.0) AS BarcodeOffsetMm,
                       COALESCE(theme, 'Night') AS Theme
                FROM lab_settings 
                ORDER BY id ASC 
                LIMIT 1;
            ";

            var settings = await conn.QueryFirstOrDefaultAsync<LabSettingsDto>(sql);
            return settings ?? new LabSettingsDto();
        }

        public async Task SaveSettingsAsync(LabSettingsDto settings, long? currentUserId = null)
        {
            using var conn = _connectionFactory.CreateConnection();

            // Ensure columns exist (automatic schema migration)
            await conn.ExecuteAsync(@"
                ALTER TABLE lab_settings ADD COLUMN IF NOT EXISTS barcode_offset_mm DOUBLE PRECISION DEFAULT 15.0;
                ALTER TABLE lab_settings ADD COLUMN IF NOT EXISTS theme TEXT DEFAULT 'Night';
            ");

            const string sql = @"
                INSERT INTO lab_settings (
                    id, lab_name_ar, lab_name_en, logo_data, address, phones, email,
                    report_header_text, report_footer_text, manager_name, manager_title,
                    signature_data, paper_size, currency, label_printer, report_printer, receipt_printer,
                    barcode_offset_mm, theme, updated_at
                )
                OVERRIDING SYSTEM VALUE
                VALUES (
                    1, @LabNameAr, @LabNameEn, @LogoData, @Address, @Phones, @Email,
                    @ReportHeaderText, @ReportFooterText, @ManagerName, @ManagerTitle,
                    @SignatureData, @PaperSize, @Currency, @LabelPrinter, @ReportPrinter, @ReceiptPrinter,
                    @BarcodeOffsetMm, @Theme, clock_timestamp()
                )
                ON CONFLICT (id) DO UPDATE
                SET lab_name_ar = EXCLUDED.lab_name_ar,
                    lab_name_en = EXCLUDED.lab_name_en,
                    logo_data = COALESCE(EXCLUDED.logo_data, lab_settings.logo_data),
                    address = EXCLUDED.address,
                    phones = EXCLUDED.phones,
                    email = EXCLUDED.email,
                    report_header_text = EXCLUDED.report_header_text,
                    report_footer_text = EXCLUDED.report_footer_text,
                    manager_name = EXCLUDED.manager_name,
                    manager_title = EXCLUDED.manager_title,
                    signature_data = COALESCE(EXCLUDED.signature_data, lab_settings.signature_data),
                    paper_size = EXCLUDED.paper_size,
                    currency = EXCLUDED.currency,
                    label_printer = EXCLUDED.label_printer,
                    report_printer = EXCLUDED.report_printer,
                    receipt_printer = EXCLUDED.receipt_printer,
                    barcode_offset_mm = EXCLUDED.barcode_offset_mm,
                    theme = EXCLUDED.theme,
                    updated_at = clock_timestamp();
            ";

            await conn.ExecuteAsync(sql, settings);
            await _authService.LogAuditAsync(currentUserId, "UPDATE_SETTINGS", "lab_settings", "1", null, settings);
        }
    }
}
