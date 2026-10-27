using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Data;

namespace Lis.Core.Services
{
    public class CalculatedComponentDto
    {
        public long Id { get; set; }
        public long ComponentId { get; set; }
        public string ComponentCode { get; set; } = string.Empty;
        public string ComponentNameEn { get; set; } = string.Empty;
        public string Formula { get; set; } = string.Empty;
        public List<string> InputComponentCodes { get; set; } = new List<string>();
    }

    public interface ICalculatedComponentService
    {
        Task<List<CalculatedComponentDto>> GetCalculatedComponentsAsync();
        Task RegisterOrUpdateCalculatedComponentAsync(string targetComponentCode, string formula, List<string> inputCodes);
        Task<int> EvaluateCalculatedComponentsForOrderAsync(long orderId, long? currentUserId);
    }

    public class CalculatedComponentService : ICalculatedComponentService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IReferenceRangeService _refRangeService;

        public CalculatedComponentService(IDbConnectionFactory connectionFactory, IReferenceRangeService refRangeService)
        {
            _connectionFactory = connectionFactory;
            _refRangeService = refRangeService;
        }

        public async Task<List<CalculatedComponentDto>> GetCalculatedComponentsAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT cc.id,
                       cc.component_id AS ComponentId,
                       c.component_code AS ComponentCode,
                       c.name_en AS ComponentNameEn,
                       cc.formula,
                       cc.input_component_ids
                FROM calculated_components cc
                JOIN components c ON c.id = cc.component_id;
            ";

            var rows = await conn.QueryAsync<dynamic>(sql);
            var list = new List<CalculatedComponentDto>();
            foreach (var r in rows)
            {
                var dto = new CalculatedComponentDto
                {
                    Id = Convert.ToInt64(r.id),
                    ComponentId = Convert.ToInt64(r.componentid),
                    ComponentCode = r.componentcode?.ToString() ?? "",
                    ComponentNameEn = r.componentnameen?.ToString() ?? "",
                    Formula = r.formula?.ToString() ?? ""
                };

                // Parse input component codes from formula tokens like [C_ALB], [C_TP]
                var matches = Regex.Matches(dto.Formula, @"\[([A-Za-z0-9_]+)\]");
                foreach (Match m in matches)
                {
                    if (m.Groups.Count > 1)
                    {
                        var code = m.Groups[1].Value.Trim();
                        if (!dto.InputComponentCodes.Contains(code))
                            dto.InputComponentCodes.Add(code);
                    }
                }

                list.Add(dto);
            }
            return list;
        }

        public async Task RegisterOrUpdateCalculatedComponentAsync(string targetComponentCode, string formula, List<string> inputCodes)
        {
            using var conn = _connectionFactory.CreateConnection();
            var compId = await conn.ExecuteScalarAsync<long?>(
                "SELECT id FROM components WHERE component_code = @Code;", new { Code = targetComponentCode.Trim() });

            if (!compId.HasValue)
            {
                throw new InvalidOperationException($"Target component '{targetComponentCode}' does not exist in components table.");
            }

            var jsonInputs = System.Text.Json.JsonSerializer.Serialize(inputCodes);
            const string sql = @"
                INSERT INTO calculated_components (component_id, formula, input_component_ids, updated_at)
                VALUES (@CompId, @Formula, @Inputs::jsonb, clock_timestamp())
                ON CONFLICT (component_id) DO UPDATE
                SET formula = EXCLUDED.formula,
                    input_component_ids = EXCLUDED.input_component_ids,
                    updated_at = clock_timestamp();
            ";

            await conn.ExecuteAsync(sql, new
            {
                CompId = compId.Value,
                Formula = formula.Trim(),
                Inputs = jsonInputs
            });
        }

        public async Task<int> EvaluateCalculatedComponentsForOrderAsync(long orderId, long? currentUserId)
        {
            using var conn = _connectionFactory.CreateConnection();

            // 1. Get patient info and order date
            const string orderSql = @"
                SELECT o.id, o.order_datetime, p.sex, p.date_of_birth
                FROM orders o
                JOIN patients p ON p.id = o.patient_id
                WHERE o.id = @OrderId;
            ";
            var orderInfo = await conn.QueryFirstOrDefaultAsync<dynamic>(orderSql, new { OrderId = orderId });
            if (orderInfo == null) return 0;

            string sex = orderInfo.sex?.ToString() ?? "M";
            DateTime dob = orderInfo.date_of_birth is DateTime dt ? dt : Convert.ToDateTime(orderInfo.date_of_birth);
            DateTime orderDt = orderInfo.order_datetime is DateTime odt ? odt : Convert.ToDateTime(orderInfo.order_datetime);

            // 2. Fetch all components in this order with their current results
            const string currentResultsSql = @"
                SELECT ot.id AS ordertestid,
                       c.id AS componentid,
                       c.component_code AS componentcode,
                       c.decimals,
                       r.id AS resultid,
                       r.value_numeric AS valuenumeric
                FROM order_tests ot
                JOIN components c ON c.test_id = ot.test_id
                LEFT JOIN results r ON r.order_test_id = ot.id AND r.component_id = c.id AND r.is_current = TRUE
                WHERE ot.order_id = @OrderId;
            ";
            var compRows = (await conn.QueryAsync<dynamic>(currentResultsSql, new { OrderId = orderId })).ToList();
            if (compRows.Count == 0) return 0;

            var numericValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var compOrderTestMap = new Dictionary<string, (long OrderTestId, long ComponentId, short? Decimals)>(StringComparer.OrdinalIgnoreCase);

            foreach (var cr in compRows)
            {
                string code = cr.componentcode?.ToString() ?? "";
                long otId = Convert.ToInt64(cr.ordertestid);
                long cId = Convert.ToInt64(cr.componentid);
                short? decimals = cr.decimals != null ? Convert.ToInt16(cr.decimals) : null;

                compOrderTestMap[code] = (otId, cId, decimals);

                var rawVal = (object?)cr.valuenumeric;
                if (rawVal != null && decimal.TryParse(rawVal.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val))
                {
                    numericValues[code] = val;
                }
            }

            // 3. Fetch calculated component definitions
            var calcDefs = await GetCalculatedComponentsAsync();
            int evaluatedCount = 0;

            foreach (var calc in calcDefs)
            {
                // Is target component part of this order?
                if (!compOrderTestMap.TryGetValue(calc.ComponentCode, out var targetInfo))
                    continue;

                // Check if all input components have numeric values
                bool allInputsAvailable = true;
                foreach (var inputCode in calc.InputComponentCodes)
                {
                    if (!numericValues.ContainsKey(inputCode))
                    {
                        allInputsAvailable = false;
                        break;
                    }
                }
                if (!allInputsAvailable) continue;

                // Evaluate formula
                decimal? computedValue = EvaluateExpression(calc.Formula, numericValues);
                if (!computedValue.HasValue) continue;

                // Round to component's decimals
                int decPlaces = targetInfo.Decimals.HasValue ? Math.Max(0, (int)targetInfo.Decimals.Value) : 1;
                decimal rounded = Math.Round(computedValue.Value, decPlaces);
                string valueText = rounded.ToString($"F{decPlaces}", CultureInfo.InvariantCulture);

                // Calculate reference range flag
                var refRange = await _refRangeService.EvaluateRangeAsync(
                    targetInfo.ComponentId, sex, dob, orderDt, rounded, conn);

                var existingId = await conn.ExecuteScalarAsync<long?>(@"
                    SELECT id FROM results 
                    WHERE order_test_id = @OrderTestId AND component_id = @ComponentId AND is_current = TRUE 
                    LIMIT 1;
                ", new { OrderTestId = targetInfo.OrderTestId, ComponentId = targetInfo.ComponentId });

                if (existingId.HasValue)
                {
                    await conn.ExecuteAsync(@"
                        UPDATE results
                        SET value_text = @ValueText,
                            value_numeric = @ValueNumeric,
                            flag = @Flag,
                            resulted_at = clock_timestamp(),
                            updated_at = clock_timestamp()
                        WHERE id = @Id;
                    ", new
                    {
                        Id = existingId.Value,
                        ValueText = valueText,
                        ValueNumeric = rounded,
                        Flag = refRange.Flag
                    });
                }
                else
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO results (
                            order_test_id, component_id, value_text, value_numeric,
                            flag, resulted_at, is_current, created_at, updated_at
                        )
                        VALUES (
                            @OrderTestId, @ComponentId, @ValueText, @ValueNumeric,
                            @Flag, clock_timestamp(), TRUE, clock_timestamp(), clock_timestamp()
                        );
                    ", new
                    {
                        OrderTestId = targetInfo.OrderTestId,
                        ComponentId = targetInfo.ComponentId,
                        ValueText = valueText,
                        ValueNumeric = rounded,
                        Flag = refRange.Flag
                    });
                }

                numericValues[calc.ComponentCode] = rounded;
                evaluatedCount++;
            }

            return evaluatedCount;
        }

        private static decimal? EvaluateExpression(string formula, Dictionary<string, decimal> values)
        {
            try
            {
                // Replace tokens [CODE] with numeric values
                string expr = Regex.Replace(formula, @"\[([A-Za-z0-9_]+)\]", m =>
                {
                    var code = m.Groups[1].Value.Trim();
                    if (values.TryGetValue(code, out var v))
                    {
                        return v.ToString(CultureInfo.InvariantCulture);
                    }
                    throw new KeyNotFoundException($"Missing value for component [{code}]");
                });

                // Compute with DataTable
                using var dt = new DataTable();
                var result = dt.Compute(expr, null);
                if (result != null && decimal.TryParse(result.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal calculated))
                {
                    return calculated;
                }
            }
            catch
            {
                // Invalid formula or division by zero
            }
            return null;
        }
    }
}
