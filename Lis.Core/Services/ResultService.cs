using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Data;

namespace Lis.Core.Services
{
    public class ResultItemDto : INotifyPropertyChanged
    {
        public long OrderTestId { get; set; }
        public long TestId { get; set; }
        public string TestCode { get; set; } = string.Empty;
        public string TestNameEn { get; set; } = string.Empty;
        public string? TestNameAr { get; set; }
        public string DisplayTestName => TestNameEn;

        public long SpecimenId { get; set; }
        public string AccessionNo { get; set; } = string.Empty;
        public string SpecimenTypeName { get; set; } = string.Empty;
        public string? TubeColor { get; set; }

        public long ComponentId { get; set; }
        public string ComponentCode { get; set; } = string.Empty;
        public string ComponentNameEn { get; set; } = string.Empty;
        public string? ComponentNameAr { get; set; }
        public string? DisplayCode { get; set; }
        public string DisplayComponentName
        {
            get
            {
                // If ComponentNameEn is empty, or matches/contains the test name, return DisplayCode (e.g. HbA1c)
                if (!string.IsNullOrWhiteSpace(TestNameEn) &&
                    !string.IsNullOrWhiteSpace(ComponentNameEn) &&
                    (string.Equals(ComponentNameEn.Trim(), TestNameEn.Trim(), StringComparison.OrdinalIgnoreCase) ||
                     TestNameEn.IndexOf(ComponentNameEn.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                     ComponentNameEn.Trim().IndexOf(TestNameEn.Trim(), StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    if (!string.IsNullOrWhiteSpace(DisplayCode))
                        return DisplayCode;
                }

                if (!string.IsNullOrWhiteSpace(ComponentNameEn))
                    return ComponentNameEn;

                if (!string.IsNullOrWhiteSpace(DisplayCode))
                    return DisplayCode;

                return ComponentCode;
            }
        }

        public long? ResultId { get; set; }

        private string? _valueText;
        public string? ValueText
        {
            get => _valueText;
            set
            {
                if (_valueText != value)
                {
                    _valueText = value;
                    OnPropertyChanged();
                    IsModified = true;
                }
            }
        }

        public decimal? ValueNumeric { get; set; }
        public string? Unit { get; set; }

        private string? _flag;
        public string? Flag
        {
            get => _flag;
            set
            {
                if (_flag != value)
                {
                    _flag = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FlagDisplayText));
                    OnPropertyChanged(nameof(FlagBadgeBrush));
                }
            }
        }

        public decimal? Low { get; set; }
        public decimal? High { get; set; }
        public decimal? CriticalLow { get; set; }
        public decimal? CriticalHigh { get; set; }
        public string? ReferenceRangeNote { get; set; }

        public string ReferenceRangeText
        {
            get
            {
                if (Low.HasValue && High.HasValue) return $"{Low} - {High}";
                if (Low.HasValue) return $">= {Low}";
                if (High.HasValue) return $"<= {High}";
                if (!string.IsNullOrWhiteSpace(ReferenceRangeNote)) return ReferenceRangeNote;
                return "N/A";
            }
        }

        public string? ResultType { get; set; } = "numeric";
        public long? InstrumentId { get; set; }
        public string? InstrumentName { get; set; }
        public DateTime? ResultedAt { get; set; }
        public DateTime? ValidatedAt { get; set; }
        public string? ValidatedByUsername { get; set; }
        public string Status { get; set; } = "Ordered";

        public bool IsModified { get; set; }

        public decimal? PreviousValueNumeric { get; set; }
        public DateTime? PreviousResultedAt { get; set; }
        public decimal? DeltaChangePct { get; set; }
        public bool IsDeltaAlert => DeltaChangePct.HasValue && Math.Abs(DeltaChangePct.Value) >= 50.0m;
        public bool IsCritical => Flag == "HH" || Flag == "LL";
        public string? DeltaDisplayText => IsDeltaAlert ? $"⚠️ تغير {DeltaChangePct:+#0.0;-#0.0;0}% عن السابق" : null;
        public string? CriticalDisplayText => IsCritical ? "🚨 قيمة حرجة (Critical Value)" : null;

        public string FlagDisplayText => Flag switch
        {
            "H"  => "مرتفع (H ⬆)",
            "HH" => "حرج مرتفع (HH ⬆⬆)",
            "L"  => "منخفض (L ⬇)",
            "LL" => "حرج منخفض (LL ⬇⬇)",
            "N"  => "طبيعي (N)",
            _    => !string.IsNullOrWhiteSpace(ValueText) ? "طبيعي (N)" : "بانتظار الفحص"
        };

        public string FlagBadgeBrush => Flag switch
        {
            "H" or "HH" => "#EF4444",   // Red
            "L" or "LL" => "#3B82F6",   // Blue
            "N"         => "#10B981",   // Green
            _           => string.IsNullOrWhiteSpace(ValueText) ? "#64748B" : "#10B981"
        };

        public bool IsValidated => ValidatedAt.HasValue || Status == "Validated";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class OrderResultsDto
    {
        public long OrderId { get; set; }
        public DateTime OrderDatetime { get; set; }
        public string OrderStatus { get; set; } = "Ordered";
        public string? ReferringDoctor { get; set; }
        public string? ClinicalNotes { get; set; }

        public long PatientId { get; set; }
        public long Mrn { get; set; }
        public string FullNameAr { get; set; } = string.Empty;
        public string? FullNameEn { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string Sex { get; set; } = "M";
        public string GenderDisplay => Sex == "F" ? "أنثى" : "ذكر";
        public string? Phone { get; set; }

        public string AgeDisplay
        {
            get
            {
                var now = DateTime.Today;
                int years = now.Year - DateOfBirth.Year;
                if (DateOfBirth.Date > now.AddYears(-years)) years--;
                if (years > 0) return $"{years} سنة";

                int months = (now.Year - DateOfBirth.Year) * 12 + now.Month - DateOfBirth.Month;
                if (now.Day < DateOfBirth.Day) months--;
                if (months > 0) return $"{months} شهر";

                int days = (int)(now - DateOfBirth.Date).TotalDays;
                return $"{Math.Max(0, days)} يوم";
            }
        }

        public List<ResultItemDto> Results { get; set; } = new List<ResultItemDto>();
        public bool IsAllValidated => Results.Count > 0 && Results.All(r => r.IsValidated);
        public bool HasAnyResults => Results.Any(r => !string.IsNullOrWhiteSpace(r.ValueText));
        public bool HasCriticalResults => Results.Any(r => r.IsCritical);
        public bool HasDeltaAlerts => Results.Any(r => r.IsDeltaAlert);
        public List<ResultItemDto> CriticalResults => Results.Where(r => r.IsCritical).ToList();
        public List<ResultItemDto> DeltaAlertResults => Results.Where(r => r.IsDeltaAlert).ToList();
    }

    public class OrderWorklistHeaderDto
    {
        public long OrderId { get; set; }
        public DateTime OrderDatetime { get; set; }
        public long Mrn { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string Sex { get; set; } = "M";
        public DateTime DateOfBirth { get; set; }
        public string Status { get; set; } = "Ordered";
        public string? TestsSummary { get; set; }
        public int ResultsCount { get; set; }
        public int TotalComponentsCount { get; set; }

        public string AgeDisplay
        {
            get
            {
                var now = DateTime.Today;
                int years = now.Year - DateOfBirth.Year;
                if (DateOfBirth.Date > now.AddYears(-years)) years--;
                if (years > 0) return $"{years} سنة";
                return "رضيع";
            }
        }

        public string StatusBadgeBrush => Status switch
        {
            "Validated"      => "#10B981", // Green
            "ResultReceived" => "#38BDF8", // Sky Blue
            "InProgress"     => "#F59E0B", // Amber
            "Collected"      => "#8B5CF6", // Purple
            "Rejected"       => "#EF4444", // Red
            _                => "#64748B"  // Slate
        };
    }

    public interface IResultService
    {
        Task<List<OrderWorklistHeaderDto>> GetOrdersForResultsAsync(string? status = null, string? search = null, DateTime? fromDate = null, DateTime? toDate = null, int limit = 150);
        Task<OrderResultsDto?> GetOrderResultsAsync(long orderId);
        Task SaveComponentResultAsync(long orderTestId, long componentId, string? valueText, long? currentUserId);
        Task SaveOrderResultsBatchAsync(long orderId, List<(long OrderTestId, long ComponentId, string? ValueText)> results, long? currentUserId);
        Task ValidateOrderResultsAsync(long orderId, long? validatorUserId);
        Task UnvalidateOrderResultsAsync(long orderId, long? currentUserId);
    }

    public class ResultService : IResultService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IAuthService _authService;
        private readonly IReferenceRangeService _refRangeService;

        public ResultService(
            IDbConnectionFactory connectionFactory,
            IAuthService authService,
            IReferenceRangeService refRangeService)
        {
            _connectionFactory = connectionFactory;
            _authService = authService;
            _refRangeService = refRangeService;
        }

        private static DateTime ParseDateTimeSafe(object? val, DateTime defaultVal)
        {
            if (val == null) return defaultVal;
            DateTime dt;
            if (val is DateTime d) dt = d;
            else if (val is DateOnly dOnly) return dOnly.ToDateTime(TimeOnly.MinValue);
            else if (DateTime.TryParse(val.ToString(), out DateTime parsed)) dt = parsed;
            else return defaultVal;

            if (dt.Kind == DateTimeKind.Utc)
                return dt.ToLocalTime();
            return dt;
        }

        public async Task<List<OrderWorklistHeaderDto>> GetOrdersForResultsAsync(
            string? status = null,
            string? search = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int limit = 150)
        {
            using var conn = _connectionFactory.CreateConnection();

            string sql = @"
                SELECT o.id AS order_id,
                       o.order_datetime,
                       o.status,
                       p.mrn,
                       p.full_name_ar AS patient_name,
                       p.sex,
                       p.date_of_birth,
                       COALESCE(string_agg(DISTINCT t.name_en, ', '), '') AS tests_summary,
                       COUNT(DISTINCT r.id) FILTER (WHERE r.id IS NOT NULL AND r.is_current = TRUE AND r.value_text IS NOT NULL AND r.value_text <> '') AS results_count,
                       COUNT(DISTINCT c.id) AS total_components_count
                FROM orders o
                JOIN patients p ON p.id = o.patient_id
                LEFT JOIN order_tests ot ON ot.order_id = o.id
                LEFT JOIN tests t ON t.id = ot.test_id
                LEFT JOIN components c ON c.test_id = t.id
                LEFT JOIN results r ON r.order_test_id = ot.id AND r.component_id = c.id AND r.is_current = TRUE
                WHERE (@Status::text IS NULL OR o.status = @Status)
                  AND (@FromDate::timestamptz IS NULL OR o.order_datetime >= @FromDate)
                  AND (@ToDate::timestamptz IS NULL OR o.order_datetime <= @ToDate)
                  AND (
                      @Search::text IS NULL OR
                      p.full_name_ar ILIKE '%' || @Search || '%' OR
                      p.mrn::text ILIKE '%' || @Search || '%' OR
                      o.id::text ILIKE '%' || @Search || '%' OR
                      EXISTS (
                          SELECT 1 FROM specimens s 
                          WHERE s.order_id = o.id AND s.accession_no ILIKE '%' || @Search || '%'
                      )
                  )
                GROUP BY o.id, p.id
                ORDER BY o.id DESC
                LIMIT @Limit;
            ";

            DateTime? fromDt = fromDate.HasValue ? fromDate.Value.Date : (DateTime?)null;
            DateTime? toDt = toDate.HasValue ? toDate.Value.Date.AddDays(1).AddTicks(-1) : (DateTime?)null;

            var rows = await conn.QueryAsync<dynamic>(sql, new
            {
                Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
                Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                FromDate = fromDt,
                ToDate = toDt,
                Limit = limit
            });

            var list = new List<OrderWorklistHeaderDto>();
            foreach (var r in rows)
            {
                DateTime dob = ParseDateTimeSafe(r.date_of_birth, DateTime.MinValue);
                DateTime orderDt = ParseDateTimeSafe(r.order_datetime, DateTime.Now);

                list.Add(new OrderWorklistHeaderDto
                {
                    OrderId = Convert.ToInt64(r.order_id),
                    OrderDatetime = orderDt,
                    Status = r.status?.ToString() ?? "Ordered",
                    Mrn = Convert.ToInt64(r.mrn),
                    PatientName = r.patient_name?.ToString() ?? "",
                    Sex = r.sex?.ToString() ?? "M",
                    DateOfBirth = dob,
                    TestsSummary = r.tests_summary?.ToString(),
                    ResultsCount = r.results_count != null ? Convert.ToInt32(r.results_count) : 0,
                    TotalComponentsCount = r.total_components_count != null ? Convert.ToInt32(r.total_components_count) : 0
                });
            }

            return list;
        }

        public async Task<OrderResultsDto?> GetOrderResultsAsync(long orderId)
        {
            using var conn = _connectionFactory.CreateConnection();

            const string orderSql = @"
                SELECT o.id AS order_id, o.order_datetime, o.status,
                       o.referring_doctor, o.clinical_notes,
                       p.id AS patient_id, p.mrn, p.full_name_ar, p.full_name_en,
                       p.date_of_birth, p.sex, p.phone
                FROM orders o
                JOIN patients p ON p.id = o.patient_id
                WHERE o.id = @OrderId;
            ";

            var oRow = await conn.QueryFirstOrDefaultAsync<dynamic>(orderSql, new { OrderId = orderId });
            if (oRow == null) return null;

            DateTime dob = ParseDateTimeSafe(oRow.date_of_birth, DateTime.MinValue);
            DateTime orderDt = ParseDateTimeSafe(oRow.order_datetime, DateTime.Now);

            var order = new OrderResultsDto
            {
                OrderId = Convert.ToInt64(oRow.order_id),
                OrderDatetime = orderDt,
                OrderStatus = oRow.status?.ToString() ?? "Ordered",
                ReferringDoctor = oRow.referring_doctor?.ToString(),
                ClinicalNotes = oRow.clinical_notes?.ToString(),
                PatientId = Convert.ToInt64(oRow.patient_id),
                Mrn = Convert.ToInt64(oRow.mrn),
                FullNameAr = oRow.full_name_ar?.ToString() ?? "",
                FullNameEn = oRow.full_name_en?.ToString(),
                DateOfBirth = dob,
                Sex = oRow.sex?.ToString() ?? "M",
                Phone = oRow.phone?.ToString()
            };

            const string resultsSql = @"
                SELECT ot.id AS ordertestid,
                       ot.status,
                       t.id AS testid,
                       t.test_code AS testcode,
                       t.name_en AS testnameen,
                       t.name_ar AS testnamear,
                       s.id AS specimenid,
                       s.accession_no AS accessionno,
                       st.name_en AS specimentypename,
                       st.color AS tubecolor,
                       c.id AS componentid,
                       c.component_code AS componentcode,
                       c.name_en AS componentnameen,
                       c.name_ar AS componentnamear,
                       c.display_code AS displaycode,
                       c.result_type AS resulttype,
                       c.unit,
                       r.id AS resultid,
                       r.value_text AS valuetext,
                       r.value_numeric AS valuenumeric,
                       r.flag,
                       r.resulted_at AS resultedat,
                       r.validated_at AS validatedat,
                       u.username AS validatedbyusername,
                       inst.name AS instrumentname
                FROM order_tests ot
                JOIN tests t ON t.id = ot.test_id
                JOIN specimens s ON s.id = ot.specimen_id
                JOIN specimen_types st ON st.id = s.specimen_type_id
                JOIN components c ON c.test_id = t.id
                LEFT JOIN results r ON r.order_test_id = ot.id AND r.component_id = c.id AND r.is_current = TRUE
                LEFT JOIN users u ON u.id = r.validated_by
                LEFT JOIN instruments inst ON inst.id = r.instrument_id
                WHERE ot.order_id = @OrderId
                ORDER BY t.sort_order, t.id, c.sort_order, c.id;
            ";

            var rawResults = (await conn.QueryAsync<dynamic>(resultsSql, new { OrderId = orderId })).ToList();

            // Fetch previous validated results for the same patient (for delta checking)
            const string prevSql = @"
                SELECT DISTINCT ON (r.component_id)
                       r.component_id,
                       r.value_numeric,
                       r.resulted_at
                FROM results r
                JOIN order_tests ot ON ot.id = r.order_test_id
                JOIN orders o ON o.id = ot.order_id
                WHERE o.patient_id = @PatientId
                  AND o.id != @OrderId
                  AND r.is_current = TRUE
                  AND (r.validated_at IS NOT NULL OR ot.status = 'Validated')
                  AND r.value_numeric IS NOT NULL
                ORDER BY r.component_id, r.resulted_at DESC NULLS LAST, r.id DESC;
            ";
            var prevList = (await conn.QueryAsync<dynamic>(prevSql, new { PatientId = order.PatientId, OrderId = orderId })).ToList();
            var prevDict = new Dictionary<long, (decimal Val, DateTime? Dt)>();
            foreach (var pRow in prevList)
            {
                long cId = Convert.ToInt64(pRow.component_id);
                decimal pVal = Convert.ToDecimal(pRow.value_numeric);
                DateTime? pDt = pRow.resulted_at is DateTime dt ? dt : null;
                prevDict[cId] = (pVal, pDt);
            }

            var list = new List<ResultItemDto>();
            foreach (var row in rawResults)
            {
                var dict = (IDictionary<string, object?>)row;

                object? GetVal(params string[] keys)
                {
                    foreach (var k in keys)
                    {
                        if (dict.TryGetValue(k, out var v) && v != null) return v;
                        if (dict.TryGetValue(k.ToLowerInvariant(), out v) && v != null) return v;
                    }
                    return null;
                }

                long compId = Convert.ToInt64(GetVal("componentid", "component_id") ?? 0);
                if (compId == 0) continue;

                decimal? numVal = null;
                var rawNum = GetVal("valuenumeric", "value_numeric");
                if (rawNum != null && decimal.TryParse(rawNum.ToString(), out var parsedNum))
                {
                    numVal = parsedNum;
                }

                string? valText = GetVal("valuetext", "value_text")?.ToString();
                string? flag = GetVal("flag")?.ToString();

                // Lookup reference range for patient
                var refRange = await _refRangeService.EvaluateRangeAsync(
                    compId,
                    order.Sex,
                    order.DateOfBirth,
                    order.OrderDatetime,
                    numVal,
                    conn);

                if (numVal.HasValue && string.IsNullOrWhiteSpace(flag))
                {
                    flag = refRange.Flag;
                }

                // Delta check against previous validated result
                decimal? prevNumeric = null;
                DateTime? prevResultedAt = null;
                decimal? deltaPct = null;

                if (prevDict.TryGetValue(compId, out var prevInfo))
                {
                    prevNumeric = prevInfo.Val;
                    prevResultedAt = prevInfo.Dt;
                    if (numVal.HasValue && prevInfo.Val != 0)
                    {
                        deltaPct = Math.Round(((numVal.Value - prevInfo.Val) / Math.Abs(prevInfo.Val)) * 100m, 1);
                    }
                }

                DateTime? resultedAt = null;
                var rawResulted = GetVal("resultedat", "resulted_at");
                if (rawResulted is DateTime rDt) resultedAt = rDt;
                else if (rawResulted != null && DateTime.TryParse(rawResulted.ToString(), out var parsedRDt)) resultedAt = parsedRDt;

                DateTime? validatedAt = null;
                var rawValidated = GetVal("validatedat", "validated_at");
                if (rawValidated is DateTime vDt) validatedAt = vDt;
                else if (rawValidated != null && DateTime.TryParse(rawValidated.ToString(), out var parsedVDt)) validatedAt = parsedVDt;

                list.Add(new ResultItemDto
                {
                    OrderTestId = Convert.ToInt64(GetVal("ordertestid", "order_test_id") ?? 0),
                    Status = GetVal("status")?.ToString() ?? "Ordered",
                    TestId = Convert.ToInt64(GetVal("testid", "test_id") ?? 0),
                    TestCode = GetVal("testcode", "test_code")?.ToString() ?? "",
                    TestNameEn = GetVal("testnameen", "name_en")?.ToString() ?? "",
                    TestNameAr = GetVal("testnamear", "name_ar")?.ToString(),
                    SpecimenId = Convert.ToInt64(GetVal("specimenid", "specimen_id") ?? 0),
                    AccessionNo = GetVal("accessionno", "accession_no")?.ToString() ?? "",
                    SpecimenTypeName = GetVal("specimentypename", "specimen_type_name")?.ToString() ?? "",
                    TubeColor = GetVal("tubecolor", "tube_color")?.ToString(),
                    ComponentId = compId,
                    ComponentCode = GetVal("componentcode", "component_code")?.ToString() ?? "",
                    ComponentNameEn = GetVal("componentnameen")?.ToString() ?? "",
                    ComponentNameAr = GetVal("componentnamear")?.ToString(),
                    DisplayCode = GetVal("displaycode", "display_code")?.ToString(),
                    ResultType = GetVal("resulttype", "result_type")?.ToString() ?? "numeric",
                    Unit = GetVal("unit")?.ToString(),
                    ResultId = GetVal("resultid", "result_id") != null ? Convert.ToInt64(GetVal("resultid", "result_id")) : null,
                    ValueText = valText,
                    ValueNumeric = numVal,
                    Flag = flag,
                    Low = refRange.Low,
                    High = refRange.High,
                    CriticalLow = refRange.CriticalLow,
                    CriticalHigh = refRange.CriticalHigh,
                    ReferenceRangeNote = refRange.Note,
                    PreviousValueNumeric = prevNumeric,
                    PreviousResultedAt = prevResultedAt,
                    DeltaChangePct = deltaPct,
                    ResultedAt = resultedAt,
                    ValidatedAt = validatedAt,
                    ValidatedByUsername = GetVal("validatedbyusername")?.ToString(),
                    InstrumentName = GetVal("instrumentname")?.ToString()
                });
            }

            order.Results = list;
            return order;
        }

        private async Task<long?> ResolveValidUserIdAsync(IDbConnection conn, long? userId, IDbTransaction? trans = null)
        {
            if (userId.HasValue)
            {
                var exists = await conn.ExecuteScalarAsync<int?>(
                    "SELECT 1 FROM users WHERE id = @Id LIMIT 1;",
                    new { Id = userId.Value },
                    trans);
                if (exists.HasValue && exists.Value == 1) return userId.Value;
            }

            var defaultId = await conn.ExecuteScalarAsync<long?>(
                "SELECT id FROM users WHERE active = TRUE ORDER BY id ASC LIMIT 1;",
                null,
                trans);
            return defaultId;
        }

        public async Task SaveComponentResultAsync(long orderTestId, long componentId, string? valueText, long? currentUserId)
        {
            long? orderId = null;
            using (var conn = (Npgsql.NpgsqlConnection)_connectionFactory.CreateConnection())
            {
                if (conn.State != ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var trans = conn.BeginTransaction();

                try
                {
                    var validUserId = await ResolveValidUserIdAsync(conn, currentUserId, trans);
                    await SaveComponentResultInternalAsync(conn, trans, orderTestId, componentId, valueText, validUserId);
                    orderId = await conn.ExecuteScalarAsync<long?>(
                        "SELECT order_id FROM order_tests WHERE id = @OtId;", new { OtId = orderTestId }, trans);
                    trans.Commit();
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }

            if (orderId.HasValue)
            {
                var calcService = new CalculatedComponentService(_connectionFactory, _refRangeService);
                await calcService.EvaluateCalculatedComponentsForOrderAsync(orderId.Value, currentUserId);
            }
        }

        public async Task SaveOrderResultsBatchAsync(
            long orderId,
            List<(long OrderTestId, long ComponentId, string? ValueText)> results,
            long? currentUserId)
        {
            if (results == null || results.Count == 0) return;

            long? validUserId = null;
            using (var conn = (Npgsql.NpgsqlConnection)_connectionFactory.CreateConnection())
            {
                if (conn.State != ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var trans = conn.BeginTransaction();

                try
                {
                    validUserId = await ResolveValidUserIdAsync(conn, currentUserId, trans);

                    foreach (var item in results)
                    {
                        await SaveComponentResultInternalAsync(conn, trans, item.OrderTestId, item.ComponentId, item.ValueText, validUserId);
                    }

                    await conn.ExecuteAsync(@"
                        UPDATE orders 
                        SET status = 'ResultReceived', updated_at = clock_timestamp() 
                        WHERE id = @OrderId AND status IN ('Ordered', 'Collected', 'InProgress');
                    ", new { OrderId = orderId }, trans);

                    trans.Commit();
                    await _authService.LogAuditAsync(validUserId, "BATCH_RESULTS_SAVE", "orders", orderId.ToString());
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }

            var calcService = new CalculatedComponentService(_connectionFactory, _refRangeService);
            await calcService.EvaluateCalculatedComponentsForOrderAsync(orderId, validUserId);
        }

        private async Task SaveComponentResultInternalAsync(
            IDbConnection conn,
            IDbTransaction trans,
            long orderTestId,
            long componentId,
            string? valueText,
            long? currentUserId)
        {
            var details = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT ot.order_id, ot.test_id, o.order_datetime, p.sex, p.date_of_birth, c.result_type, c.unit
                FROM order_tests ot
                JOIN orders o ON o.id = ot.order_id
                JOIN patients p ON p.id = o.patient_id
                JOIN components c ON c.id = @ComponentId
                WHERE ot.id = @OrderTestId;
            ", new { OrderTestId = orderTestId, ComponentId = componentId }, trans);

            if (details == null) return;

            DateTime dob = ParseDateTimeSafe(details.date_of_birth, DateTime.MinValue);
            DateTime orderDt = ParseDateTimeSafe(details.order_datetime, DateTime.Now);

            decimal? numVal = null;
            if (!string.IsNullOrWhiteSpace(valueText))
            {
                if (decimal.TryParse(valueText, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedVal) ||
                    decimal.TryParse(valueText, out parsedVal))
                {
                    numVal = parsedVal;
                }
            }

            string? flag = null;
            if (numVal.HasValue)
            {
                var refRange = await _refRangeService.EvaluateRangeAsync(
                    componentId,
                    (string)details.sex,
                    dob,
                    orderDt,
                    numVal,
                    conn,
                    trans);
                flag = refRange.Flag;
            }

            if (flag != "N" && flag != "H" && flag != "L" && flag != "HH" && flag != "LL")
            {
                flag = null;
            }

            var existing = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT id, value_text, value_numeric, flag, instrument_id
                FROM results
                WHERE order_test_id = @OrderTestId AND component_id = @ComponentId AND is_current = TRUE;
            ", new { OrderTestId = orderTestId, ComponentId = componentId }, trans);

            if (existing != null)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO result_history (result_id, value_text, value_numeric, flag, instrument_id, changed_at, changed_by, reason)
                    VALUES (@ResultId, @ValueText, @ValueNum, @Flag, @InstId, clock_timestamp(), @UserId, 'Manual Entry / Update');
                ", new
                {
                    ResultId = (long)existing.id,
                    ValueText = (string?)existing.value_text,
                    ValueNum = (decimal?)existing.value_numeric,
                    Flag = (string?)existing.flag,
                    InstId = (long?)existing.instrument_id,
                    UserId = currentUserId
                }, trans);

                await conn.ExecuteAsync(@"
                    UPDATE results
                    SET value_text = @ValueText,
                        value_numeric = @ValueNumeric,
                        unit = COALESCE(unit, @Unit),
                        flag = @Flag,
                        resulted_at = clock_timestamp(),
                        updated_at = clock_timestamp()
                    WHERE id = @ResultId;
                ", new
                {
                    ResultId = (long)existing.id,
                    ValueText = valueText,
                    ValueNumeric = numVal,
                    Unit = (string?)details.unit,
                    Flag = flag
                }, trans);
            }
            else
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO results (order_test_id, component_id, value_text, value_numeric, unit, flag, resulted_at, is_current, updated_at)
                    VALUES (@OrderTestId, @ComponentId, @ValueText, @ValueNumeric, @Unit, @Flag, clock_timestamp(), TRUE, clock_timestamp());
                ", new
                {
                    OrderTestId = orderTestId,
                    ComponentId = componentId,
                    ValueText = valueText,
                    ValueNumeric = numVal,
                    Unit = (string?)details.unit,
                    Flag = flag
                }, trans);
            }

            await conn.ExecuteAsync(@"
                UPDATE order_tests 
                SET status = 'ResultReceived', updated_at = clock_timestamp() 
                WHERE id = @OrderTestId AND status IN ('Ordered', 'Collected', 'InProgress');
            ", new { OrderTestId = orderTestId }, trans);

            long orderId = (long)details.order_id;
            await conn.ExecuteAsync(@"
                UPDATE specimens 
                SET status = 'ResultReceived', updated_at = clock_timestamp()
                WHERE order_id = @OrderId AND status IN ('Ordered', 'Collected', 'InProgress');
            ", new { OrderId = orderId }, trans);
        }

        public async Task ValidateOrderResultsAsync(long orderId, long? validatorUserId)
        {
            using var conn = (Npgsql.NpgsqlConnection)_connectionFactory.CreateConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
            }
            using var trans = conn.BeginTransaction();

            try
            {
                var validUserId = await ResolveValidUserIdAsync(conn, validatorUserId, trans);

                await conn.ExecuteAsync(@"
                    UPDATE results
                    SET validated_by = @UserId, validated_at = clock_timestamp(), updated_at = clock_timestamp()
                    WHERE order_test_id IN (SELECT id FROM order_tests WHERE order_id = @OrderId) AND is_current = TRUE;

                    UPDATE order_tests
                    SET status = 'Validated', updated_at = clock_timestamp()
                    WHERE order_id = @OrderId;

                    UPDATE specimens
                    SET status = 'Validated', updated_at = clock_timestamp()
                    WHERE order_id = @OrderId;

                    UPDATE orders
                    SET status = 'Validated', updated_at = clock_timestamp()
                    WHERE id = @OrderId;
                ", new { OrderId = orderId, UserId = validUserId }, trans);

                trans.Commit();
                await _authService.LogAuditAsync(validUserId, "VALIDATE_ORDER_RESULTS", "orders", orderId.ToString());
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        public async Task UnvalidateOrderResultsAsync(long orderId, long? currentUserId)
        {
            using var conn = (Npgsql.NpgsqlConnection)_connectionFactory.CreateConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
            }
            using var trans = conn.BeginTransaction();

            try
            {
                var validUserId = await ResolveValidUserIdAsync(conn, currentUserId, trans);

                await conn.ExecuteAsync(@"
                    UPDATE results
                    SET validated_by = NULL, validated_at = NULL, updated_at = clock_timestamp()
                    WHERE order_test_id IN (SELECT id FROM order_tests WHERE order_id = @OrderId) AND is_current = TRUE;

                    UPDATE order_tests
                    SET status = 'ResultReceived', updated_at = clock_timestamp()
                    WHERE order_id = @OrderId;

                    UPDATE specimens
                    SET status = 'ResultReceived', updated_at = clock_timestamp()
                    WHERE order_id = @OrderId;

                    UPDATE orders
                    SET status = 'ResultReceived', updated_at = clock_timestamp()
                    WHERE id = @OrderId;
                ", new { OrderId = orderId }, trans);

                trans.Commit();
                await _authService.LogAuditAsync(validUserId, "UNVALIDATE_ORDER_RESULTS", "orders", orderId.ToString());
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }
    }
}
