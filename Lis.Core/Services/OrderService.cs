using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Data;
using Lis.Core.Models;

namespace Lis.Core.Services
{
    public class CreateOrderRequest
    {
        public long PatientId { get; set; }
        public string? ReferringDoctor { get; set; }
        public string? ClinicalNotes { get; set; }
        public List<long> TestIds { get; set; } = new List<long>();
        public decimal Discount { get; set; } = 0;
        public decimal InitialPayment { get; set; } = 0;
        public string PaymentMethod { get; set; } = "Cash";
    }

    public class OrderDetailsDto
    {
        public Order Order { get; set; } = new Order();
        public Patient Patient { get; set; } = new Patient();
        public List<SpecimenDto> Specimens { get; set; } = new List<SpecimenDto>();
        public Invoice? Invoice { get; set; }
    }

    public class SpecimenDto
    {
        public Specimen Specimen { get; set; } = new Specimen();
        public string SpecimenTypeName { get; set; } = string.Empty;
        public string? Color { get; set; }
        public List<OrderTestDto> Tests { get; set; } = new List<OrderTestDto>();
    }

    public class OrderTestDto
    {
        public long OrderTestId { get; set; }
        public long TestId { get; set; }
        public string TestCode { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public decimal Price { get; set; }
        public string Status { get; set; } = "Ordered";
    }

    public class Invoice
    {
        public long Id { get; set; }
        public long OrderId { get; set; }
        public long InvoiceNo { get; set; }
        public decimal Total { get; set; }
        public decimal Discount { get; set; }
        public decimal Net { get; set; }
        public decimal Paid { get; set; }
        public decimal Balance { get; set; }
        public string Status { get; set; } = "Unpaid";
        public long? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public interface IOrderService
    {
        Task<OrderDetailsDto> CreateOrderAsync(CreateOrderRequest request, long? currentUserId = null);
        Task<OrderDetailsDto?> GetOrderByIdAsync(long orderId);
        Task<OrderDetailsDto?> GetOrderByAccessionAsync(string accessionNo);
        Task<IEnumerable<dynamic>> GetOrdersListAsync(string? status = null, DateTime? date = null, string? query = null, int limit = 100);
        Task<IEnumerable<dynamic>> GetDepartmentWorklistAsync(long? departmentId = null, string? status = null, int limit = 100);
        Task MarkSpecimenCollectedAsync(long specimenId, long? currentUserId = null);
        Task RejectSpecimenAsync(long specimenId, long reasonId, string? notes, bool recollect, long? currentUserId = null);
        Task CancelOrderAsync(long orderId, string reason, long? currentUserId = null);
        Task<Specimen> AddTestToOrderAsync(long orderId, long testId, long? existingSpecimenId = null, long? currentUserId = null);
        Task RemoveTestFromOrderAsync(long orderId, long orderTestId, long? currentUserId = null);
    }

    public class OrderService : IOrderService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IAccessionService _accessionService;
        private readonly IAuthService _authService;

        public OrderService(
            IDbConnectionFactory connectionFactory, 
            IAccessionService accessionService,
            IAuthService authService)
        {
            _connectionFactory = connectionFactory;
            _accessionService = accessionService;
            _authService = authService;
        }

        public async Task<OrderDetailsDto> CreateOrderAsync(CreateOrderRequest request, long? currentUserId = null)
        {
            if (request.PatientId <= 0) throw new ArgumentException("Invalid Patient ID", nameof(request.PatientId));
            if (request.TestIds == null || request.TestIds.Count == 0) throw new ArgumentException("At least one test must be selected.", nameof(request.TestIds));

            using var conn = (Npgsql.NpgsqlConnection)_connectionFactory.CreateConnection();
            using var trans = conn.BeginTransaction();

            try
            {
                // 1. Fetch Tests information including price and specimen_type_id
                var tests = (await conn.QueryAsync<TestItem>(@"
                    SELECT id, test_code AS TestCode, name_en AS NameEn, name_ar AS NameAr,
                           specimen_type_id AS SpecimenTypeId, price, department_id AS DepartmentId
                    FROM tests
                    WHERE id = ANY(@Ids) AND active = TRUE;
                ", new { Ids = request.TestIds.Distinct().ToArray() }, trans)).ToList();

                if (tests.Count == 0) throw new InvalidOperationException("None of the selected tests are valid or active.");

                // 2. Count patient's prior visits to determine visit_no
                var priorOrdersCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM orders WHERE patient_id = @PatientId;",
                    new { PatientId = request.PatientId }, trans);

                int visitNo = priorOrdersCount + 1;

                // 3. Create Order
                const string insertOrderSql = @"
                    INSERT INTO orders (patient_id, visit_no, order_datetime, referring_doctor, clinical_notes, status, created_by, updated_at)
                    VALUES (@PatientId, @VisitNo, clock_timestamp(), @ReferringDoctor, @ClinicalNotes, 'Ordered', @CreatedBy, clock_timestamp())
                    RETURNING id, patient_id AS PatientId, visit_no AS VisitNo, order_datetime AS OrderDatetime,
                              referring_doctor AS ReferringDoctor, clinical_notes AS ClinicalNotes, status, created_by AS CreatedBy;
                ";

                var order = await conn.QuerySingleAsync<Order>(insertOrderSql, new
                {
                    PatientId = request.PatientId,
                    VisitNo = visitNo,
                    ReferringDoctor = request.ReferringDoctor?.Trim(),
                    ClinicalNotes = request.ClinicalNotes?.Trim(),
                    CreatedBy = currentUserId
                }, trans);
                order.OrderDatetime = ToLocal(order.OrderDatetime);

                // 4. Auto-group tests by specimen_type_id
                // If a test has no specimen_type_id, use fallback specimen type
                var fallbackSpecimenTypeId = await conn.ExecuteScalarAsync<long>(
                    "SELECT id FROM specimen_types ORDER BY id ASC LIMIT 1;", transaction: trans);

                var groups = tests.GroupBy(t => t.SpecimenTypeId ?? fallbackSpecimenTypeId);

                var createdSpecimens = new List<SpecimenDto>();

                foreach (var group in groups)
                {
                    long specTypeId = group.Key;
                    var specTypeInfo = await conn.QuerySingleAsync<dynamic>(
                        "SELECT name_en, color FROM specimen_types WHERE id = @Id;",
                        new { Id = specTypeId }, trans);

                    // Generate unique 6-digit accession
                    string accession = await _accessionService.GenerateNextAccessionAsync(conn, trans);

                    const string insertSpecimenSql = @"
                        INSERT INTO specimens (order_id, specimen_type_id, accession_no, status, issued_at, archived, updated_at)
                        VALUES (@OrderId, @SpecimenTypeId, @AccessionNo, 'Ordered', clock_timestamp(), FALSE, clock_timestamp())
                        RETURNING id, order_id AS OrderId, specimen_type_id AS SpecimenTypeId, accession_no AS AccessionNo,
                                  status, issued_at AS IssuedAt, archived, created_at AS CreatedAt;
                    ";

                    var specimen = await conn.QuerySingleAsync<Specimen>(insertSpecimenSql, new
                    {
                        OrderId = order.Id,
                        SpecimenTypeId = specTypeId,
                        AccessionNo = accession
                    }, trans);

                    var specDto = new SpecimenDto
                    {
                        Specimen = specimen,
                        SpecimenTypeName = (string)specTypeInfo.name_en,
                        Color = (string?)specTypeInfo.color
                    };

                    // Insert order_tests for each test in this group
                    foreach (var test in group)
                    {
                        const string insertOrderTestSql = @"
                            INSERT INTO order_tests (order_id, specimen_id, test_id, status, updated_at)
                            VALUES (@OrderId, @SpecimenId, @TestId, 'Ordered', clock_timestamp())
                            RETURNING id;
                        ";

                        long orderTestId = await conn.ExecuteScalarAsync<long>(insertOrderTestSql, new
                        {
                            OrderId = order.Id,
                            SpecimenId = specimen.Id,
                            TestId = test.Id
                        }, trans);

                        specDto.Tests.Add(new OrderTestDto
                        {
                            OrderTestId = orderTestId,
                            TestId = test.Id,
                            TestCode = test.TestCode,
                            NameEn = test.NameEn,
                            NameAr = test.NameAr,
                            Price = test.Price,
                            Status = "Ordered"
                        });
                    }

                    createdSpecimens.Add(specDto);
                }

                // 5. Billing: Create Invoice with snapshot prices
                decimal total = tests.Sum(t => t.Price);
                decimal discount = Math.Max(0, request.Discount);
                decimal net = Math.Max(0, total - discount);
                decimal paid = Math.Min(net, Math.Max(0, request.InitialPayment));
                decimal balance = net - paid;

                string invoiceStatus = balance == 0 ? (net > 0 ? "Paid" : "Paid") : (paid > 0 ? "Partial" : "Unpaid");

                const string insertInvoiceSql = @"
                    INSERT INTO invoices (order_id, total, discount, net, paid, balance, status, created_by, created_at)
                    VALUES (@OrderId, @Total, @Discount, @Net, @Paid, @Balance, @Status, @CreatedBy, clock_timestamp())
                    RETURNING id, order_id AS OrderId, invoice_no AS InvoiceNo, total, discount, net, paid, balance, status, created_by AS CreatedBy, created_at AS CreatedAt;
                ";

                var invoice = await conn.QuerySingleAsync<Invoice>(insertInvoiceSql, new
                {
                    OrderId = order.Id,
                    Total = total,
                    Discount = discount,
                    Net = net,
                    Paid = paid,
                    Balance = balance,
                    Status = invoiceStatus,
                    CreatedBy = currentUserId
                }, trans);

                // Insert snapshot invoice_items
                foreach (var test in tests)
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO invoice_items (invoice_id, test_id, price_at_time)
                        VALUES (@InvoiceId, @TestId, @PriceAtTime);
                    ", new
                    {
                        InvoiceId = invoice.Id,
                        TestId = test.Id,
                        PriceAtTime = test.Price
                    }, trans);
                }

                // If initial payment was made, record in payments table
                if (paid > 0)
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO payments (invoice_id, amount, method, user_id, created_at)
                        VALUES (@InvoiceId, @Amount, @Method, @UserId, clock_timestamp());
                    ", new
                    {
                        InvoiceId = invoice.Id,
                        Amount = paid,
                        Method = request.PaymentMethod ?? "Cash",
                        UserId = currentUserId
                    }, trans);
                }

                trans.Commit();

                await _authService.LogAuditAsync(currentUserId, "CREATE_ORDER", "orders", order.Id.ToString(), null, new { order.Id, Accessions = createdSpecimens.Select(s => s.Specimen.AccessionNo) });

                var patient = await conn.QuerySingleAsync<Patient>(
                    "SELECT id, mrn, full_name_ar AS FullNameAr, full_name_en AS FullNameEn, sex, date_of_birth AS DateOfBirth, phone FROM patients WHERE id = @Id;",
                    new { Id = request.PatientId });

                return new OrderDetailsDto
                {
                    Order = order,
                    Patient = patient,
                    Specimens = createdSpecimens,
                    Invoice = invoice
                };
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        public async Task<OrderDetailsDto?> GetOrderByIdAsync(long orderId)
        {
            using var conn = _connectionFactory.CreateConnection();
            var order = await conn.QueryFirstOrDefaultAsync<Order>(@"
                SELECT id, patient_id AS PatientId, visit_no AS VisitNo, order_datetime AS OrderDatetime,
                       referring_doctor AS ReferringDoctor, clinical_notes AS ClinicalNotes, status, created_by AS CreatedBy
                FROM orders WHERE id = @Id;
            ", new { Id = orderId });

            if (order == null) return null;
            order.OrderDatetime = ToLocal(order.OrderDatetime);

            var patient = await conn.QuerySingleAsync<Patient>(@"
                SELECT id, mrn, full_name_ar AS FullNameAr, full_name_en AS FullNameEn, sex, date_of_birth AS DateOfBirth, phone
                FROM patients WHERE id = @Id;
            ", new { Id = order.PatientId });

            var specimens = (await conn.QueryAsync<dynamic>(@"
                SELECT s.id, s.order_id, s.specimen_type_id, s.accession_no, s.status, s.collected_at, s.issued_at, s.archived,
                       st.name_en AS specimen_type_name, st.color
                FROM specimens s
                JOIN specimen_types st ON st.id = s.specimen_type_id
                WHERE s.order_id = @OrderId
                ORDER BY s.id ASC;
            ", new { OrderId = orderId })).ToList();

            var specDtos = new List<SpecimenDto>();

            foreach (var s in specimens)
            {
                var spec = new Specimen
                {
                    Id = (long)s.id,
                    OrderId = (long)s.order_id,
                    SpecimenTypeId = (long)s.specimen_type_id,
                    AccessionNo = (string)s.accession_no,
                    Status = (string)s.status,
                    CollectedAt = s.collected_at != null ? ToLocal((DateTime)s.collected_at) : null,
                    IssuedAt = ToLocal((DateTime)s.issued_at),
                    Archived = (bool)s.archived
                };

                var tests = (await conn.QueryAsync<OrderTestDto>(@"
                    SELECT ot.id AS OrderTestId, t.id AS TestId, t.test_code AS TestCode,
                           t.name_en AS NameEn, t.name_ar AS NameAr, t.price, ot.status
                    FROM order_tests ot
                    JOIN tests t ON t.id = ot.test_id
                    WHERE ot.specimen_id = @SpecimenId
                    ORDER BY ot.id ASC;
                ", new { SpecimenId = spec.Id })).ToList();

                specDtos.Add(new SpecimenDto
                {
                    Specimen = spec,
                    SpecimenTypeName = (string)s.specimen_type_name,
                    Color = (string?)s.color,
                    Tests = tests
                });
            }

            var invoice = await conn.QueryFirstOrDefaultAsync<Invoice>(@"
                SELECT id, order_id AS OrderId, invoice_no AS InvoiceNo, total, discount, net, paid, balance, status, created_by AS CreatedBy, created_at AS CreatedAt
                FROM invoices WHERE order_id = @OrderId;
            ", new { OrderId = orderId });

            return new OrderDetailsDto
            {
                Order = order,
                Patient = patient,
                Specimens = specDtos,
                Invoice = invoice
            };
        }

        public async Task<OrderDetailsDto?> GetOrderByAccessionAsync(string accessionNo)
        {
            using var conn = _connectionFactory.CreateConnection();
            var orderId = await conn.ExecuteScalarAsync<long?>(@"
                SELECT order_id FROM specimens WHERE accession_no = @AccessionNo AND archived = FALSE LIMIT 1;
            ", new { AccessionNo = accessionNo.Trim() });

            if (!orderId.HasValue) return null;

            return await GetOrderByIdAsync(orderId.Value);
        }

        public static DateTime ToLocal(DateTime dt)
        {
            if (dt == DateTime.MinValue || dt == DateTime.MaxValue)
                return dt;
            if (dt.Kind == DateTimeKind.Utc)
                return dt.ToLocalTime();
            if (dt.Kind == DateTimeKind.Unspecified)
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();
            return dt;
        }

        public async Task<IEnumerable<dynamic>> GetOrdersListAsync(string? status = null, DateTime? date = null, string? query = null, int limit = 100)
        {
            using var conn = _connectionFactory.CreateConnection();

            DateTime? fromDate = date?.Date;
            DateTime? toDate = date?.Date.AddDays(1);

            const string sql = @"
                SELECT o.id, o.order_datetime, o.status, o.referring_doctor,
                       p.mrn, p.full_name_ar, p.full_name_en, p.phone, p.sex, p.date_of_birth,
                       inv.net AS total_amount, inv.status AS invoice_status,
                       COUNT(s.id) AS specimen_count
                FROM orders o
                JOIN patients p ON p.id = o.patient_id
                LEFT JOIN invoices inv ON inv.order_id = o.id
                LEFT JOIN specimens s ON s.order_id = o.id
                WHERE (@Status IS NULL OR o.status = @Status)
                  AND (@FromDate::timestamptz IS NULL OR o.order_datetime >= @FromDate)
                  AND (@ToDate::timestamptz IS NULL OR o.order_datetime < @ToDate)
                  AND (@Query IS NULL OR p.full_name_ar ILIKE '%' || @Query || '%' OR p.phone ILIKE '%' || @Query || '%' OR p.mrn::text = @Query)
                GROUP BY o.id, p.id, inv.id
                ORDER BY o.id DESC
                LIMIT @Limit;
            ";

            var rows = (await conn.QueryAsync<dynamic>(sql, new
            {
                Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
                FromDate = fromDate,
                ToDate = toDate,
                Query = string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
                Limit = limit
            })).ToList();

            foreach (var r in rows)
            {
                if (r is IDictionary<string, object> dict && dict.TryGetValue("order_datetime", out var odt) && odt is DateTime dt)
                {
                    dict["order_datetime"] = ToLocal(dt);
                }
            }

            return rows;
        }

        public async Task<IEnumerable<dynamic>> GetDepartmentWorklistAsync(long? departmentId = null, string? status = null, int limit = 100)
        {
            using var conn = _connectionFactory.CreateConnection();

            const string sql = @"
                SELECT s.id AS specimen_id, s.accession_no, s.status AS specimen_status, 
                       COALESCE(s.collected_at, o.order_datetime) AS collected_at,
                       st.name_en AS specimen_type_name, st.color,
                       o.id AS order_id, o.order_datetime,
                       p.mrn, p.full_name_ar, p.sex, p.date_of_birth,
                       string_agg(DISTINCT t.test_code, ', ') AS test_names,
                       COALESCE(inv.paid, 0) AS paid_amount,
                       inv.status AS invoice_status
                FROM specimens s
                JOIN orders o ON o.id = s.order_id
                JOIN patients p ON p.id = o.patient_id
                JOIN specimen_types st ON st.id = s.specimen_type_id
                JOIN order_tests ot ON ot.specimen_id = s.id
                JOIN tests t ON t.id = ot.test_id
                JOIN invoices inv ON inv.order_id = o.id
                WHERE s.archived = FALSE
                  AND (@Status IS NULL OR s.status = @Status)
                  AND (@DepartmentId IS NULL OR t.department_id = @DepartmentId)
                GROUP BY s.id, st.id, o.id, p.id, inv.id
                ORDER BY s.id DESC
                LIMIT @Limit;
            ";

            var rows = (await conn.QueryAsync<dynamic>(sql, new
            {
                Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
                DepartmentId = departmentId,
                Limit = limit
            })).ToList();

            foreach (var r in rows)
            {
                if (r is IDictionary<string, object> dict)
                {
                    if (dict.TryGetValue("collected_at", out var cat) && cat is DateTime dtCol)
                        dict["collected_at"] = ToLocal(dtCol);
                    if (dict.TryGetValue("order_datetime", out var odt) && odt is DateTime dtOrd)
                        dict["order_datetime"] = ToLocal(dtOrd);
                }
            }

            return rows;
        }

        public async Task MarkSpecimenCollectedAsync(long specimenId, long? currentUserId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE specimens
                SET status = 'Collected',
                    collected_at = clock_timestamp(),
                    updated_at = clock_timestamp()
                WHERE id = @Id;

                UPDATE order_tests
                SET status = 'Collected',
                    updated_at = clock_timestamp()
                WHERE specimen_id = @Id AND status = 'Ordered';
            ", new { Id = specimenId });

            // If all specimens in the order are collected, update order status to Collected
            var orderId = await conn.ExecuteScalarAsync<long>("SELECT order_id FROM specimens WHERE id = @Id;", new { Id = specimenId });
            await conn.ExecuteAsync(@"
                UPDATE orders
                SET status = 'Collected', updated_at = clock_timestamp()
                WHERE id = @OrderId AND NOT EXISTS (
                    SELECT 1 FROM specimens WHERE order_id = @OrderId AND status = 'Ordered'
                );
            ", new { OrderId = orderId });

            await _authService.LogAuditAsync(currentUserId, "SPECIMEN_COLLECTED", "specimens", specimenId.ToString());
        }

        public async Task RejectSpecimenAsync(long specimenId, long reasonId, string? notes, bool recollect, long? currentUserId = null)
        {
            using var conn = (Npgsql.NpgsqlConnection)_connectionFactory.CreateConnection();
            using var trans = conn.BeginTransaction();

            try
            {
                // Record rejection
                await conn.ExecuteAsync(@"
                    INSERT INTO specimen_rejections (specimen_id, reason_id, notes, user_id, created_at)
                    VALUES (@SpecimenId, @ReasonId, @Notes, @UserId, clock_timestamp());

                    UPDATE specimens
                    SET status = 'Rejected', updated_at = clock_timestamp()
                    WHERE id = @SpecimenId;

                    UPDATE order_tests
                    SET status = 'Rejected', updated_at = clock_timestamp()
                    WHERE specimen_id = @SpecimenId;
                ", new
                {
                    SpecimenId = specimenId,
                    ReasonId = reasonId,
                    Notes = notes?.Trim(),
                    UserId = currentUserId
                }, trans);

                if (recollect)
                {
                    // Create replacement specimen tube with new accession
                    var oldSpec = await conn.QuerySingleAsync<dynamic>(
                        "SELECT order_id, specimen_type_id FROM specimens WHERE id = @Id;",
                        new { Id = specimenId }, trans);

                    string newAccession = await _accessionService.GenerateNextAccessionAsync(conn, trans);

                    long newSpecimenId = await conn.ExecuteScalarAsync<long>(@"
                        INSERT INTO specimens (order_id, specimen_type_id, accession_no, status, issued_at, archived, updated_at)
                        VALUES (@OrderId, @SpecimenTypeId, @AccessionNo, 'Ordered', clock_timestamp(), FALSE, clock_timestamp())
                        RETURNING id;
                    ", new
                    {
                        OrderId = (long)oldSpec.order_id,
                        SpecimenTypeId = (long)oldSpec.specimen_type_id,
                        AccessionNo = newAccession
                    }, trans);

                    // Re-assign the rejected tests to the new specimen with status 'Ordered'
                    await conn.ExecuteAsync(@"
                        INSERT INTO order_tests (order_id, specimen_id, test_id, status, updated_at)
                        SELECT order_id, @NewSpecimenId, test_id, 'Ordered', clock_timestamp()
                        FROM order_tests
                        WHERE specimen_id = @OldSpecimenId;
                    ", new
                    {
                        NewSpecimenId = newSpecimenId,
                        OldSpecimenId = specimenId
                    }, trans);
                }

                trans.Commit();
                await _authService.LogAuditAsync(currentUserId, "SPECIMEN_REJECTED", "specimens", specimenId.ToString(), null, new { reasonId, notes, recollect });
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        public async Task CancelOrderAsync(long orderId, string reason, long? currentUserId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE orders SET status = 'Cancelled', updated_at = clock_timestamp() WHERE id = @Id;
                UPDATE specimens SET status = 'Cancelled', updated_at = clock_timestamp() WHERE order_id = @Id;
                UPDATE order_tests SET status = 'Cancelled', updated_at = clock_timestamp() WHERE order_id = @Id;
                UPDATE invoices SET status = 'Cancelled' WHERE order_id = @Id;
            ", new { Id = orderId });

            await _authService.LogAuditAsync(currentUserId, "CANCEL_ORDER", "orders", orderId.ToString(), null, new { reason });
        }

        public async Task<Specimen> AddTestToOrderAsync(long orderId, long testId, long? existingSpecimenId = null, long? currentUserId = null)
        {
            using var conn = (Npgsql.NpgsqlConnection)_connectionFactory.CreateConnection();
            using var trans = conn.BeginTransaction();

            try
            {
                var test = await conn.QuerySingleAsync<TestItem>(
                    "SELECT id, test_code AS TestCode, name_en AS NameEn, price, specimen_type_id AS SpecimenTypeId FROM tests WHERE id = @Id;",
                    new { Id = testId }, trans);

                Specimen targetSpecimen;

                if (existingSpecimenId.HasValue)
                {
                    targetSpecimen = await conn.QuerySingleAsync<Specimen>(
                        "SELECT id, order_id AS OrderId, specimen_type_id AS SpecimenTypeId, accession_no AS AccessionNo, status FROM specimens WHERE id = @Id;",
                        new { Id = existingSpecimenId.Value }, trans);
                }
                else
                {
                    var specTypeId = test.SpecimenTypeId ?? await conn.ExecuteScalarAsync<long>("SELECT id FROM specimen_types LIMIT 1;", transaction: trans);

                    // Check if there is already an active specimen for this order with the same specimen_type_id
                    var existingSpec = await conn.QueryFirstOrDefaultAsync<Specimen>(@"
                        SELECT id, order_id AS OrderId, specimen_type_id AS SpecimenTypeId, accession_no AS AccessionNo, status
                        FROM specimens
                        WHERE order_id = @OrderId AND specimen_type_id = @SpecimenTypeId AND status != 'Cancelled' AND status != 'Rejected'
                        ORDER BY id ASC
                        LIMIT 1;
                    ", new { OrderId = orderId, SpecimenTypeId = specTypeId }, trans);

                    if (existingSpec != null)
                    {
                        targetSpecimen = existingSpec;
                    }
                    else
                    {
                        string accession = await _accessionService.GenerateNextAccessionAsync(conn, trans);

                        targetSpecimen = await conn.QuerySingleAsync<Specimen>(@"
                            INSERT INTO specimens (order_id, specimen_type_id, accession_no, status, issued_at, archived, updated_at)
                            VALUES (@OrderId, @SpecimenTypeId, @AccessionNo, 'Ordered', clock_timestamp(), FALSE, clock_timestamp())
                            RETURNING id, order_id AS OrderId, specimen_type_id AS SpecimenTypeId, accession_no AS AccessionNo, status;
                        ", new
                        {
                            OrderId = orderId,
                            SpecimenTypeId = specTypeId,
                            AccessionNo = accession
                        }, trans);
                    }
                }

                // Add order_test
                await conn.ExecuteAsync(@"
                    INSERT INTO order_tests (order_id, specimen_id, test_id, status, updated_at)
                    VALUES (@OrderId, @SpecimenId, @TestId, 'Ordered', clock_timestamp());
                ", new
                {
                    OrderId = orderId,
                    SpecimenId = targetSpecimen.Id,
                    TestId = test.Id
                }, trans);

                // Update invoice total and balance
                var invoice = await conn.QueryFirstOrDefaultAsync<Invoice>(
                    "SELECT id, total, discount, net, paid, balance FROM invoices WHERE order_id = @OrderId;",
                    new { OrderId = orderId }, trans);

                if (invoice != null)
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO invoice_items (invoice_id, test_id, price_at_time)
                        VALUES (@InvoiceId, @TestId, @Price);

                        UPDATE invoices
                        SET total = total + @Price,
                            net = (total + @Price) - discount,
                            balance = ((total + @Price) - discount) - paid,
                            status = CASE WHEN (((total + @Price) - discount) - paid) <= 0 THEN 'Paid' ELSE 'Partial' END
                        WHERE id = @InvoiceId;
                    ", new
                    {
                        InvoiceId = invoice.Id,
                        TestId = test.Id,
                        Price = test.Price
                    }, trans);
                }

                trans.Commit();
                await _authService.LogAuditAsync(currentUserId, "ADD_TEST_TO_ORDER", "order_tests", orderId.ToString(), null, new { testId, specimenId = targetSpecimen.Id });

                return targetSpecimen;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        public async Task RemoveTestFromOrderAsync(long orderId, long orderTestId, long? currentUserId = null)
        {
            using var conn = (Npgsql.NpgsqlConnection)_connectionFactory.CreateConnection();
            using var trans = conn.BeginTransaction();

            try
            {
                // 1. Get the order_test and test info
                var orderTest = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT ot.id, ot.order_id, ot.specimen_id, ot.test_id, t.price
                    FROM order_tests ot
                    JOIN tests t ON t.id = ot.test_id
                    WHERE ot.id = @OrderTestId AND ot.order_id = @OrderId;
                ", new { OrderTestId = orderTestId, OrderId = orderId }, trans);

                if (orderTest == null)
                {
                    throw new InvalidOperationException("لم يتم العثور على الفحص المحدد في هذا الطلب.");
                }

                long testId = (long)orderTest.test_id;
                long specimenId = (long)orderTest.specimen_id;
                decimal price = (decimal)orderTest.price;

                // 2. Delete test results if any exist
                await conn.ExecuteAsync("DELETE FROM results WHERE order_test_id = @OrderTestId;", new { OrderTestId = orderTestId }, trans);

                // 3. Delete the order_test
                await conn.ExecuteAsync("DELETE FROM order_tests WHERE id = @OrderTestId;", new { OrderTestId = orderTestId }, trans);

                // 4. Delete one matching invoice_items entry
                await conn.ExecuteAsync(@"
                    DELETE FROM invoice_items
                    WHERE id = (
                        SELECT ii.id FROM invoice_items ii
                        JOIN invoices inv ON inv.id = ii.invoice_id
                        WHERE inv.order_id = @OrderId AND ii.test_id = @TestId
                        LIMIT 1
                    );
                ", new { OrderId = orderId, TestId = testId }, trans);

                // 5. Update invoice
                await conn.ExecuteAsync(@"
                    UPDATE invoices
                    SET total = GREATEST(0, total - @Price),
                        net = GREATEST(0, (total - @Price) - discount),
                        balance = GREATEST(0, ((total - @Price) - discount) - paid),
                        status = CASE 
                            WHEN GREATEST(0, ((total - @Price) - discount) - paid) <= 0 THEN 'Paid' 
                            WHEN paid > 0 THEN 'Partial' 
                            ELSE 'Unpaid' 
                        END
                    WHERE order_id = @OrderId;
                ", new { OrderId = orderId, Price = price }, trans);

                // 6. If this specimen has no remaining tests, delete it
                var remainingTests = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM order_tests WHERE specimen_id = @SpecimenId;",
                    new { SpecimenId = specimenId }, trans);

                if (remainingTests == 0)
                {
                    await conn.ExecuteAsync("DELETE FROM specimens WHERE id = @SpecimenId;", new { SpecimenId = specimenId }, trans);
                }

                trans.Commit();
                await _authService.LogAuditAsync(currentUserId, "REMOVE_TEST_FROM_ORDER", "order_tests", orderId.ToString(), null, new { orderTestId, testId, specimenId, price });
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }
    }
}
