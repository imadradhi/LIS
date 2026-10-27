using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Models;
using Lis.Core.Services;

namespace Lis.Tests
{
    public class Phase2Tests
    {
        private readonly TestDatabaseFixture _fixture;
        private readonly IAuthService _authService;
        private readonly IPatientService _patientService;
        private readonly IOrderService _orderService;
        private readonly IBillingService _billingService;

        public Phase2Tests(TestDatabaseFixture fixture)
        {
            _fixture = fixture;
            _authService = new AuthService(_fixture.ConnectionFactory);
            _patientService = new PatientService(_fixture.ConnectionFactory, _authService);
            var accessionService = new AccessionService(_fixture.ConnectionFactory);
            _orderService = new OrderService(_fixture.ConnectionFactory, accessionService, _authService);
            _billingService = new BillingService(_fixture.ConnectionFactory, _authService);
        }

        private async Task<long> GetAdminUserIdAsync()
        {
            await _authService.EnsureDefaultAdminUserAsync();
            using var conn = _fixture.ConnectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<long>("SELECT id FROM users WHERE username = 'admin';");
        }

        public async Task Test8_PatientRegistration_AssignsPermanentMrn_AndCalculatesAge()
        {
            long adminId = await GetAdminUserIdAsync();

            var p = new Patient
            {
                FullNameAr = "علي كاظم جاسم",
                FullNameEn = "Ali Kadhim Jassim",
                Sex = "M",
                DateOfBirth = new DateTime(1985, 5, 20),
                Phone = "07712345678"
            };

            var created = await _patientService.CreatePatientAsync(p, adminId);

            Assert.True(created.Id > 0);
            Assert.True(created.Mrn >= 100001);
            Assert.Equal("علي كاظم جاسم", created.FullNameAr);

            var age = _patientService.CalculateAge(created.DateOfBirth, new DateTime(2026, 5, 20));
            Assert.Equal(41, age.Years);
            Assert.Equal(0, age.Months);
            Assert.Equal(0, age.Days);
        }

        public async Task Test9_OrderCreation_AutoGroupsBySpecimenType_ProducesSeparateAccessions()
        {
            long adminId = await GetAdminUserIdAsync();

            using var conn = _fixture.ConnectionFactory.CreateConnection();

            // 1. Ensure we have 2 tests with 2 different specimen types:
            // Test 1: CBC (EDTA Whole Blood)
            var cbcTest = await conn.QuerySingleAsync<TestItem>(
                "SELECT id, test_code AS TestCode, specimen_type_id AS SpecimenTypeId, price FROM tests WHERE test_code = 'T_CBC_360';");

            // Test 2: Chemistry Glucose (Serum)
            var serumTypeId = await conn.ExecuteScalarAsync<long?>(
                "SELECT id FROM specimen_types WHERE name_en = 'Serum';");

            if (!serumTypeId.HasValue)
            {
                serumTypeId = await conn.ExecuteScalarAsync<long>(
                    "INSERT INTO specimen_types (name_en, name_ar, color) VALUES ('Serum', 'مصل', '#FBBF24') RETURNING id;");
            }

            var glucoseTestId = await conn.ExecuteScalarAsync<long?>(
                "SELECT id FROM tests WHERE test_code = 'T_GLU';");

            if (!glucoseTestId.HasValue)
            {
                glucoseTestId = await conn.ExecuteScalarAsync<long>(@"
                    INSERT INTO tests (test_code, name_en, name_ar, specimen_type_id, price, active)
                    VALUES ('T_GLU', 'Fasting Blood Glucose', 'سكر الدم الصائم', @SpecTypeId, 3000, TRUE)
                    RETURNING id;
                ", new { SpecTypeId = serumTypeId.Value });
            }

            // 2. Register Patient
            var patient = await _patientService.CreatePatientAsync(new Patient
            {
                FullNameAr = "زينب حيدر صادق",
                FullNameEn = "Zainab Haider Sadiq",
                Sex = "F",
                DateOfBirth = new DateTime(1992, 10, 15),
                Phone = "07809988776"
            }, adminId);

            // 3. Create Order with both tests (2 different specimen types)
            var orderReq = new CreateOrderRequest
            {
                PatientId = patient.Id,
                ReferringDoctor = "د. كمال النعيمي",
                TestIds = new List<long> { cbcTest.Id, glucoseTestId.Value },
                Discount = 500,
                InitialPayment = 2000,
                PaymentMethod = "Cash"
            };

            var orderDetails = await _orderService.CreateOrderAsync(orderReq, adminId);

            // Phase 2 Acceptance Criteria Check:
            // Registering tests from 2 specimen types MUST produce 2 specimens (tubes)
            Assert.Equal(2, orderDetails.Specimens.Count);

            var acc1 = orderDetails.Specimens[0].Specimen.AccessionNo;
            var acc2 = orderDetails.Specimens[1].Specimen.AccessionNo;

            // Must have exactly 6-digit accessions
            Assert.Equal(6, acc1.Length);
            Assert.Equal(6, acc2.Length);
            // Must have different accessions
            Assert.NotEqual(acc1, acc2);

            // Invoice checks
            Assert.NotNull(orderDetails.Invoice);
            decimal expectedTotal = cbcTest.Price + 3000;
            decimal expectedNet = expectedTotal - 500;
            decimal expectedBalance = expectedNet - 2000;

            Assert.Equal(expectedTotal, orderDetails.Invoice.Total);
            Assert.Equal(expectedNet, orderDetails.Invoice.Net);
            Assert.Equal(2000m, orderDetails.Invoice.Paid);
            Assert.Equal(expectedBalance, orderDetails.Invoice.Balance);
        }

        public async Task Test10_ReturningPatient_PreservesSameMrn_WithNewAccessions()
        {
            long adminId = await GetAdminUserIdAsync();

            // Register Patient
            var patient = await _patientService.CreatePatientAsync(new Patient
            {
                FullNameAr = "حسين عبد الله كريم",
                FullNameEn = "Hussein Abdullah Karim",
                Sex = "M",
                DateOfBirth = new DateTime(1980, 3, 12),
                Phone = "07501122334"
            }, adminId);

            long originalMrn = patient.Mrn;

            using var conn = _fixture.ConnectionFactory.CreateConnection();
            var cbcTestId = await conn.ExecuteScalarAsync<long>("SELECT id FROM tests WHERE test_code = 'T_CBC_360';");

            // Visit 1
            var order1 = await _orderService.CreateOrderAsync(new CreateOrderRequest
            {
                PatientId = patient.Id,
                TestIds = new List<long> { cbcTestId }
            }, adminId);

            Assert.Equal(1, order1.Order.VisitNo);
            string accVisit1 = order1.Specimens[0].Specimen.AccessionNo;

            // Visit 2 (Returning Patient)
            var order2 = await _orderService.CreateOrderAsync(new CreateOrderRequest
            {
                PatientId = patient.Id,
                TestIds = new List<long> { cbcTestId }
            }, adminId);

            // Acceptance Criteria: Returning patient keeps the SAME MRN
            Assert.Equal(originalMrn, order2.Patient.Mrn);
            // Visit number increments
            Assert.Equal(2, order2.Order.VisitNo);
            // Generates new accession for the new visit's tube
            string accVisit2 = order2.Specimens[0].Specimen.AccessionNo;
            Assert.NotEqual(accVisit1, accVisit2);
        }

        public async Task Test11_Billing_PaymentsAndDailyCashReport()
        {
            var today = DateTime.Today;
            var report = await _billingService.GetDailyCashReportAsync(today);

            Assert.NotNull(report);
            Assert.True(report.TotalInvoiced >= 0);
            Assert.True(report.TotalCollected >= 0);
        }

        public async Task Test12_SpecimenRejection_WithRecollection_CreatesNewAccession()
        {
            long adminId = await GetAdminUserIdAsync();

            using var conn = _fixture.ConnectionFactory.CreateConnection();
            var cbcTestId = await conn.ExecuteScalarAsync<long>("SELECT id FROM tests WHERE test_code = 'T_CBC_360';");

            var patient = await _patientService.CreatePatientAsync(new Patient
            {
                FullNameAr = "مروة طارق فاضل",
                Sex = "F",
                DateOfBirth = new DateTime(1998, 7, 22),
                Phone = "07705544332"
            }, adminId);

            var order = await _orderService.CreateOrderAsync(new CreateOrderRequest
            {
                PatientId = patient.Id,
                TestIds = new List<long> { cbcTestId }
            }, adminId);

            var oldSpecimen = order.Specimens[0].Specimen;

            // Reject with recollection (e.g. Hemolyzed sample = reason 1)
            await _orderService.RejectSpecimenAsync(oldSpecimen.Id, 1, "عينة متحللة", recollect: true, currentUserId: adminId);

            // Fetch refreshed order details
            var refreshed = await _orderService.GetOrderByIdAsync(order.Order.Id);
            Assert.NotNull(refreshed);

            // There should now be 2 specimens: 1 Rejected, and 1 new Ordered (replacement)
            Assert.Equal(2, refreshed.Specimens.Count);

            var rejectedSpec = refreshed.Specimens.FirstOrDefault(s => s.Specimen.Id == oldSpecimen.Id);
            var replacementSpec = refreshed.Specimens.FirstOrDefault(s => s.Specimen.Id != oldSpecimen.Id);

            Assert.NotNull(rejectedSpec);
            Assert.NotNull(replacementSpec);
            Assert.Equal("Rejected", rejectedSpec!.Specimen.Status);
            Assert.Equal("Ordered", replacementSpec!.Specimen.Status);
            Assert.NotEqual(rejectedSpec.Specimen.AccessionNo, replacementSpec.Specimen.AccessionNo);
        }

        public async Task Test12_1_LabSettings_SaveAndRetrieve_WithIdentityOverride()
        {
            long adminId = await GetAdminUserIdAsync();
            var settingsService = new LabSettingsService(_fixture.ConnectionFactory, _authService);

            var settings = new LabSettingsDto
            {
                LabNameAr = "مختبر النقاء التخصصي",
                LabNameEn = "Al-Naqaa Specialized Lab",
                Address = "بغداد - الكرادة",
                Phones = "07700000000",
                Email = "info@alnaqaa-lab.com",
                ManagerName = "د. سمير أحمد",
                ManagerTitle = "استشاري علم الأمراض",
                ReportHeaderText = "قسم التحليلات السريرية المتقدمة",
                ReportFooterText = "الفحص معتمد تشخيصياً",
                LabelPrinter = "HPRT SL32",
                ReportPrinter = "Canon G3070 series",
                Currency = "IQD"
            };

            settings.BarcodeOffsetMm = 15.0;
            settings.Theme = "Light";

            await settingsService.SaveSettingsAsync(settings, adminId);

            var retrieved = await settingsService.GetSettingsAsync();
            Assert.Equal("مختبر النقاء التخصصي", retrieved.LabNameAr);
            Assert.Equal("Al-Naqaa Specialized Lab", retrieved.LabNameEn);
            Assert.Equal("HPRT SL32", retrieved.LabelPrinter);
            Assert.Equal("Canon G3070 series", retrieved.ReportPrinter);
            Assert.Equal(15.0, retrieved.BarcodeOffsetMm);
            Assert.Equal("Light", retrieved.Theme);
        }

        public Task Test12_2_SpecimenLabel_ExtractsFirstTwoNamesCorrectly()
        {
            // Simple names
            Assert.Equal("زينب حيدر", Lis.Core.Services.SpecimenLabelData.GetFirstTwoNames("زينب حيدر علي كاظم"));
            Assert.Equal("علي حسن", Lis.Core.Services.SpecimenLabelData.GetFirstTwoNames("علي حسن"));
            Assert.Equal("محمد", Lis.Core.Services.SpecimenLabelData.GetFirstTwoNames("محمد"));
            Assert.Equal("", Lis.Core.Services.SpecimenLabelData.GetFirstTwoNames(""));

            // Compound first names
            Assert.Equal("عبد الله أحمد", Lis.Core.Services.SpecimenLabelData.GetFirstTwoNames("عبد الله أحمد كريم"));
            Assert.Equal("أبو بكر سالم", Lis.Core.Services.SpecimenLabelData.GetFirstTwoNames("أبو بكر سالم عمر"));

            // Compound second names
            Assert.Equal("أحمد عبد الرحمن", Lis.Core.Services.SpecimenLabelData.GetFirstTwoNames("أحمد عبد الرحمن شاكر"));

            // Compound first and second names
            Assert.Equal("عبد الله عبد الرحمن", Lis.Core.Services.SpecimenLabelData.GetFirstTwoNames("عبد الله عبد الرحمن علي"));

            return Task.CompletedTask;
        }

        public async Task Test12_3_AuthService_AdminLogin_WithAdmin123()
        {
            var session = await _authService.LoginAsync("admin", "Admin123");
            Assert.NotNull(session);
            Assert.Equal("admin", session!.Username);

            // Invalid password test
            var invalidSession = await _authService.LoginAsync("admin", "WrongPassword");
            Assert.True(invalidSession == null);
        }
    }
}
