using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Data;

namespace Lis.Core.Services
{
    public class DailyCashReportDto
    {
        public DateTime Date { get; set; }
        public decimal TotalInvoiced { get; set; }
        public decimal TotalDiscount { get; set; }
        public decimal TotalNet { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal TotalCashCollected { get; set; }
        public decimal TotalCardCollected { get; set; }
        public decimal TotalBalance { get; set; }
        public List<UserCashSummaryDto> UserSummaries { get; set; } = new List<UserCashSummaryDto>();
    }

    public class UserCashSummaryDto
    {
        public long? UserId { get; set; }
        public string Username { get; set; } = "غير محدد";
        public string FullName { get; set; } = "غير محدد";
        public decimal CashCollected { get; set; }
        public decimal CardCollected { get; set; }
        public decimal TotalCollected { get; set; }
        public int TransactionsCount { get; set; }

        public string FormattedCash => $"{CashCollected:#,##0} IQD";
        public string FormattedCard => $"{CardCollected:#,##0} IQD";
        public string FormattedTotal => $"{TotalCollected:#,##0} IQD";
        public string FormattedTxCount => $"{TransactionsCount} عملية";
    }

    public interface IBillingService
    {
        Task RecordPaymentAsync(long invoiceId, decimal amount, string method, long? currentUserId = null);
        Task ApplyDiscountAsync(long invoiceId, decimal discountAmount, long? currentUserId = null);
        Task<DailyCashReportDto> GetDailyCashReportAsync(DateTime date);
        Task<IEnumerable<dynamic>> GetUnpaidInvoicesAsync(int limit = 100);
    }

    public class BillingService : IBillingService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IAuthService _authService;

        public BillingService(IDbConnectionFactory connectionFactory, IAuthService authService)
        {
            _connectionFactory = connectionFactory;
            _authService = authService;
        }

        public async Task RecordPaymentAsync(long invoiceId, decimal amount, string method, long? currentUserId = null)
        {
            if (amount <= 0) throw new ArgumentException("Payment amount must be greater than zero.", nameof(amount));

            using var conn = (Npgsql.NpgsqlConnection)_connectionFactory.CreateConnection();
            using var trans = conn.BeginTransaction();

            try
            {
                var inv = await conn.QuerySingleAsync<dynamic>(
                    "SELECT id, net, paid, balance, status FROM invoices WHERE id = @Id;",
                    new { Id = invoiceId }, trans);

                decimal currentPaid = (decimal)inv.paid;
                decimal currentNet = (decimal)inv.net;
                decimal newPaid = currentPaid + amount;
                decimal newBalance = Math.Max(0, currentNet - newPaid);
                string newStatus = newBalance == 0 ? "Paid" : "Partial";

                await conn.ExecuteAsync(@"
                    INSERT INTO payments (invoice_id, amount, method, user_id, created_at)
                    VALUES (@InvoiceId, @Amount, @Method, @UserId, clock_timestamp());

                    UPDATE invoices
                    SET paid = @Paid,
                        balance = @Balance,
                        status = @Status
                    WHERE id = @InvoiceId;
                ", new
                {
                    InvoiceId = invoiceId,
                    Amount = amount,
                    Method = method ?? "Cash",
                    UserId = currentUserId,
                    Paid = newPaid,
                    Balance = newBalance,
                    Status = newStatus
                }, trans);

                trans.Commit();
                await _authService.LogAuditAsync(currentUserId, "RECORD_PAYMENT", "payments", invoiceId.ToString(), null, new { amount, method, newBalance });
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        public async Task ApplyDiscountAsync(long invoiceId, decimal discountAmount, long? currentUserId = null)
        {
            if (discountAmount < 0) throw new ArgumentException("Discount cannot be negative.", nameof(discountAmount));

            using var conn = _connectionFactory.CreateConnection();
            var inv = await conn.QuerySingleAsync<dynamic>(
                "SELECT id, total, paid FROM invoices WHERE id = @Id;",
                new { Id = invoiceId });

            decimal total = (decimal)inv.total;
            decimal paid = (decimal)inv.paid;
            decimal net = Math.Max(0, total - discountAmount);
            decimal balance = Math.Max(0, net - paid);
            string status = balance == 0 ? "Paid" : (paid > 0 ? "Partial" : "Unpaid");

            await conn.ExecuteAsync(@"
                UPDATE invoices
                SET discount = @Discount,
                    net = @Net,
                    balance = @Balance,
                    status = @Status
                WHERE id = @Id;
            ", new
            {
                Id = invoiceId,
                Discount = discountAmount,
                Net = net,
                Balance = balance,
                Status = status
            });

            await _authService.LogAuditAsync(currentUserId, "APPLY_DISCOUNT", "invoices", invoiceId.ToString(), null, new { discountAmount, net, balance });
        }

        public async Task<DailyCashReportDto> GetDailyCashReportAsync(DateTime date)
        {
            using var conn = _connectionFactory.CreateConnection();

            var totals = await conn.QuerySingleAsync<dynamic>(@"
                SELECT COALESCE(SUM(total), 0) AS total_invoiced,
                       COALESCE(SUM(discount), 0) AS total_discount,
                       COALESCE(SUM(net), 0) AS total_net,
                       COALESCE(SUM(paid), 0) AS total_collected,
                       COALESCE(SUM(balance), 0) AS total_balance
                FROM invoices
                WHERE created_at::date = @Date::date AND status != 'Cancelled';
            ", new { Date = date.Date });

            var userRows = (await conn.QueryAsync<dynamic>(@"
                SELECT p.user_id,
                       u.username,
                       u.full_name,
                       COALESCE(SUM(CASE WHEN p.method = 'Cash' THEN p.amount ELSE 0 END), 0) AS cash_collected,
                       COALESCE(SUM(CASE WHEN p.method = 'Card' THEN p.amount ELSE 0 END), 0) AS card_collected,
                       COALESCE(SUM(p.amount), 0) AS total_collected,
                       COUNT(p.id) AS tx_count
                FROM payments p
                LEFT JOIN users u ON u.id = p.user_id
                WHERE p.created_at::date = @Date::date
                GROUP BY p.user_id, u.username, u.full_name
                ORDER BY total_collected DESC;
            ", new { Date = date.Date })).ToList();

            var report = new DailyCashReportDto
            {
                Date = date.Date,
                TotalInvoiced = (decimal)totals.total_invoiced,
                TotalDiscount = (decimal)totals.total_discount,
                TotalNet = (decimal)totals.total_net,
                TotalCollected = (decimal)totals.total_collected,
                TotalCashCollected = userRows.Sum(u => (decimal)u.cash_collected),
                TotalCardCollected = userRows.Sum(u => (decimal)u.card_collected),
                TotalBalance = (decimal)totals.total_balance
            };

            foreach (var r in userRows)
            {
                report.UserSummaries.Add(new UserCashSummaryDto
                {
                    UserId = (long?)r.user_id,
                    Username = (string?)r.username ?? "غير محدد",
                    FullName = (string?)r.full_name ?? "غير محدد",
                    CashCollected = (decimal)r.cash_collected,
                    CardCollected = (decimal)r.card_collected,
                    TotalCollected = (decimal)r.total_collected,
                    TransactionsCount = (int)r.tx_count
                });
            }

            return report;
        }

        public async Task<IEnumerable<dynamic>> GetUnpaidInvoicesAsync(int limit = 100)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT inv.id, inv.invoice_no, inv.total, inv.discount, inv.net, inv.paid, inv.balance, inv.status, inv.created_at,
                       o.id AS order_id, o.order_datetime,
                       p.mrn, p.full_name_ar, p.phone
                FROM invoices inv
                JOIN orders o ON o.id = inv.order_id
                JOIN patients p ON p.id = o.patient_id
                WHERE inv.balance > 0 AND inv.status IN ('Unpaid','Partial')
                ORDER BY inv.id DESC
                LIMIT @Limit;
            ";
            return await conn.QueryAsync<dynamic>(sql, new { Limit = limit });
        }
    }
}
