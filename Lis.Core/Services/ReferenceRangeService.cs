using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Data;

namespace Lis.Core.Services
{
    public class ReferenceRangeLookupResult
    {
        public bool HasRange { get; set; }
        public decimal? Low { get; set; }
        public decimal? High { get; set; }
        public decimal? CriticalLow { get; set; }
        public decimal? CriticalHigh { get; set; }
        public string? Flag { get; set; } // 'N','H','L','HH','LL' or null
        public string? Note { get; set; }
    }

    public interface IReferenceRangeService
    {
        Task<ReferenceRangeLookupResult> EvaluateRangeAsync(
            long componentId,
            string sex,
            DateTime dateOfBirth,
            DateTime orderDate,
            decimal? numericValue,
            IDbConnection? connection = null,
            IDbTransaction? transaction = null);
    }

    public class ReferenceRangeService : IReferenceRangeService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public ReferenceRangeService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<ReferenceRangeLookupResult> EvaluateRangeAsync(
            long componentId,
            string sex,
            DateTime dateOfBirth,
            DateTime orderDate,
            decimal? numericValue,
            IDbConnection? connection = null,
            IDbTransaction? transaction = null)
        {
            // Normalize sex to 'M' or 'F'
            var s = (sex ?? "M").Trim().ToUpperInvariant();
            if (s != "M" && s != "F") s = "M";

            // Age in days at order date
            int ageDays = (int)(orderDate.Date - dateOfBirth.Date).TotalDays;
            if (ageDays < 0) ageDays = 0;

            bool localConn = false;
            IDbConnection conn = connection!;

            if (conn == null)
            {
                conn = _connectionFactory.CreateConnection();
                localConn = true;
            }

            try
            {
                // Exact reference range lookup query specified in Section 4 of LIS_Project_Plan.md
                const string query = @"
                    SELECT low, high, critical_low, critical_high, note
                    FROM reference_ranges
                    WHERE component_id = @ComponentId
                      AND sex IN (@Sex, 'B')
                      AND age_from_days <= @AgeDays
                      AND (age_to_days IS NULL OR @AgeDays < age_to_days)
                    ORDER BY (sex = @Sex) DESC
                    LIMIT 1;
                ";

                var range = await conn.QueryFirstOrDefaultAsync<dynamic>(query, new
                {
                    ComponentId = componentId,
                    Sex = s,
                    AgeDays = ageDays
                }, transaction);

                var result = new ReferenceRangeLookupResult();

                if (range == null)
                {
                    // No range found for this age/sex (e.g. pediatric when only adult range exists)
                    result.HasRange = false;
                    result.Flag = null;
                    return result;
                }

                result.HasRange = true;
                result.Low = (decimal?)range.low;
                result.High = (decimal?)range.high;
                result.CriticalLow = (decimal?)range.critical_low;
                result.CriticalHigh = (decimal?)range.critical_high;
                result.Note = (string?)range.note;

                // Evaluate flags if numeric value is present
                if (numericValue.HasValue)
                {
                    decimal val = numericValue.Value;

                    if (result.CriticalLow.HasValue && val < result.CriticalLow.Value)
                    {
                        result.Flag = "LL";
                    }
                    else if (result.CriticalHigh.HasValue && val > result.CriticalHigh.Value)
                    {
                        result.Flag = "HH";
                    }
                    else if (result.Low.HasValue && val < result.Low.Value)
                    {
                        result.Flag = "L";
                    }
                    else if (result.High.HasValue && val > result.High.Value)
                    {
                        result.Flag = "H";
                    }
                    else
                    {
                        result.Flag = "N";
                    }
                }

                return result;
            }
            finally
            {
                if (localConn)
                {
                    conn.Dispose();
                }
            }
        }
    }
}
