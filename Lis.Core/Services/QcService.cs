using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Data;

namespace Lis.Core.Services
{
    public class QcMaterialDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string LotNo { get; set; } = string.Empty;
        public short Level { get; set; } = 1; // 1: Normal, 2: Abnormal/High, 3: Low
        public DateTime ExpiryDate { get; set; } = DateTime.UtcNow.AddMonths(6);
        public bool Active { get; set; } = true;
        public int TargetsCount { get; set; }

        public string LevelDisplay => Level switch
        {
            1 => "Level 1 (طبيعي / Normal)",
            2 => "Level 2 (مرتفع / High)",
            3 => "Level 3 (منخفض / Low)",
            _ => $"Level {Level}"
        };

        public string DisplayName => $"{Name} - Lot: {LotNo} ({LevelDisplay})";
    }

    public class QcTargetDto
    {
        public long Id { get; set; }
        public long QcMaterialId { get; set; }
        public string? MaterialName { get; set; }
        public string? LotNo { get; set; }
        public short Level { get; set; }
        public long InstrumentId { get; set; }
        public string? InstrumentName { get; set; }
        public string? InstrumentCode { get; set; }
        public long ComponentId { get; set; }
        public string? ComponentCode { get; set; }
        public string? ComponentName { get; set; }
        public decimal Mean { get; set; }
        public decimal Sd { get; set; }
        public string? Unit { get; set; }

        public decimal CvPercent => Mean != 0 ? Math.Round((Sd / Mean) * 100m, 2) : 0;
        public decimal Plus1Sd => Mean + Sd;
        public decimal Minus1Sd => Mean - Sd;
        public decimal Plus2Sd => Mean + (2 * Sd);
        public decimal Minus2Sd => Mean - (2 * Sd);
        public decimal Plus3Sd => Mean + (3 * Sd);
        public decimal Minus3Sd => Mean - (3 * Sd);
    }

    public class QcResultDto
    {
        public long Id { get; set; }
        public long InstrumentId { get; set; }
        public string? InstrumentName { get; set; }
        public long? QcMaterialId { get; set; }
        public string? MaterialName { get; set; }
        public string? LotNo { get; set; }
        public short? Level { get; set; }
        public long ComponentId { get; set; }
        public string? ComponentCode { get; set; }
        public string? ComponentName { get; set; }
        public decimal Value { get; set; }
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public string? RuleViolation { get; set; }
        public bool Accepted { get; set; } = true;
        public string? CorrectiveAction { get; set; }
        public long? UserId { get; set; }
        public string? UserName { get; set; }

        // Evaluation stats
        public decimal? Mean { get; set; }
        public decimal? Sd { get; set; }
        public decimal? ZScore => (Sd.HasValue && Sd.Value > 0 && Mean.HasValue) 
            ? Math.Round((Value - Mean.Value) / Sd.Value, 2) 
            : null;

        public bool HasViolation => !string.IsNullOrEmpty(RuleViolation);
        public bool IsCriticalViolation => RuleViolation is "1-3s" or "2-2s" or "R-4s" or "4-1s" or "10-x";
    }

    public class WestgardEvaluation
    {
        public string? RuleViolated { get; set; }
        public bool IsRejection { get; set; }
        public bool IsWarning { get; set; }
        public string? Message { get; set; }
    }

    public class TodayQcStatusDto
    {
        public long InstrumentId { get; set; }
        public long ComponentId { get; set; }
        public string Status { get; set; } = "NotRun"; // 'Passed', 'Warning', 'Failed', 'NotRun'
        public DateTime? LastRunAt { get; set; }
        public string? LastViolation { get; set; }
        public int TotalRunsToday { get; set; }

        public bool IsSafeToValidate => Status is "Passed" or "Warning";
    }

    public interface IQcService
    {
        // Materials
        Task<IEnumerable<QcMaterialDto>> GetMaterialsAsync(bool activeOnly = false);
        Task<QcMaterialDto?> GetMaterialByIdAsync(long id);
        Task<long> SaveMaterialAsync(QcMaterialDto material, long? userId = null);
        Task<bool> DeleteMaterialAsync(long id, long? userId = null);

        // Targets
        Task<IEnumerable<QcTargetDto>> GetTargetsAsync(long? materialId = null, long? instrumentId = null, long? componentId = null);
        Task<QcTargetDto?> GetTargetAsync(long materialId, long instrumentId, long componentId);
        Task<long> SaveTargetAsync(QcTargetDto target, long? userId = null);
        Task<bool> DeleteTargetAsync(long id, long? userId = null);

        // Results & Levey-Jennings
        Task<IEnumerable<QcResultDto>> GetResultsAsync(long instrumentId, long componentId, long? materialId = null, DateTime? fromDate = null, DateTime? toDate = null);
        Task<long> RecordResultAsync(QcResultDto result, long? userId = null);
        Task<bool> UpdateResultActionAsync(long resultId, bool accepted, string? correctiveAction, long? userId = null);

        // Rules & Status
        WestgardEvaluation EvaluateWestgard(decimal currentValue, decimal mean, decimal sd, IEnumerable<decimal> recentValues);
        Task<TodayQcStatusDto> GetTodayQcStatusAsync(long instrumentId, long componentId);
    }

    public class QcService : IQcService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public QcService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        #region Materials

        public async Task<IEnumerable<QcMaterialDto>> GetMaterialsAsync(bool activeOnly = false)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT m.id, m.name, m.lot_no AS LotNo, m.level, m.expiry_date AS ExpiryDate, m.active,
                       (SELECT COUNT(*) FROM qc_targets t WHERE t.qc_material_id = m.id) AS TargetsCount
                FROM qc_materials m
                WHERE (@ActiveOnly = FALSE OR m.active = TRUE)
                ORDER BY m.active DESC, m.expiry_date ASC, m.name ASC;
            ";
            return await conn.QueryAsync<QcMaterialDto>(sql, new { ActiveOnly = activeOnly });
        }

        public async Task<QcMaterialDto?> GetMaterialByIdAsync(long id)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT m.id, m.name, m.lot_no AS LotNo, m.level, m.expiry_date AS ExpiryDate, m.active,
                       (SELECT COUNT(*) FROM qc_targets t WHERE t.qc_material_id = m.id) AS TargetsCount
                FROM qc_materials m
                WHERE m.id = @Id;
            ";
            return await conn.QuerySingleOrDefaultAsync<QcMaterialDto>(sql, new { Id = id });
        }

        public async Task<long> SaveMaterialAsync(QcMaterialDto material, long? userId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            if (material.Id > 0)
            {
                string sql = @"
                    UPDATE qc_materials
                    SET name = @Name, lot_no = @LotNo, level = @Level, expiry_date = @ExpiryDate, active = @Active, updated_at = clock_timestamp()
                    WHERE id = @Id;
                ";
                await conn.ExecuteAsync(sql, material);
                return material.Id;
            }
            else
            {
                string sql = @"
                    INSERT INTO qc_materials (name, lot_no, level, expiry_date, active, created_at, updated_at)
                    VALUES (@Name, @LotNo, @Level, @ExpiryDate, @Active, clock_timestamp(), clock_timestamp())
                    RETURNING id;
                ";
                return await conn.ExecuteScalarAsync<long>(sql, material);
            }
        }

        public async Task<bool> DeleteMaterialAsync(long id, long? userId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = "DELETE FROM qc_materials WHERE id = @Id;";
            int affected = await conn.ExecuteAsync(sql, new { Id = id });
            return affected > 0;
        }

        #endregion

        #region Targets

        public async Task<IEnumerable<QcTargetDto>> GetTargetsAsync(long? materialId = null, long? instrumentId = null, long? componentId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT t.id, t.qc_material_id AS QcMaterialId, m.name AS MaterialName, m.lot_no AS LotNo, m.level AS Level,
                       t.instrument_id AS InstrumentId, ins.name AS InstrumentName, ins.code AS InstrumentCode,
                       t.component_id AS ComponentId, c.display_code AS ComponentCode, c.name_en AS ComponentName,
                       t.mean, t.sd, t.unit
                FROM qc_targets t
                JOIN qc_materials m ON m.id = t.qc_material_id
                JOIN instruments ins ON ins.id = t.instrument_id
                JOIN components c ON c.id = t.component_id
                WHERE (@MaterialId IS NULL OR t.qc_material_id = @MaterialId)
                  AND (@InstrumentId IS NULL OR t.instrument_id = @InstrumentId)
                  AND (@ComponentId IS NULL OR t.component_id = @ComponentId)
                ORDER BY ins.name ASC, c.sort_order ASC, m.level ASC;
            ";
            return await conn.QueryAsync<QcTargetDto>(sql, new { MaterialId = materialId, InstrumentId = instrumentId, ComponentId = componentId });
        }

        public async Task<QcTargetDto?> GetTargetAsync(long materialId, long instrumentId, long componentId)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT t.id, t.qc_material_id AS QcMaterialId, m.name AS MaterialName, m.lot_no AS LotNo, m.level AS Level,
                       t.instrument_id AS InstrumentId, ins.name AS InstrumentName, ins.code AS InstrumentCode,
                       t.component_id AS ComponentId, c.display_code AS ComponentCode, c.name_en AS ComponentName,
                       t.mean, t.sd, t.unit
                FROM qc_targets t
                JOIN qc_materials m ON m.id = t.qc_material_id
                JOIN instruments ins ON ins.id = t.instrument_id
                JOIN components c ON c.id = t.component_id
                WHERE t.qc_material_id = @MaterialId AND t.instrument_id = @InstrumentId AND t.component_id = @ComponentId;
            ";
            return await conn.QuerySingleOrDefaultAsync<QcTargetDto>(sql, new { MaterialId = materialId, InstrumentId = instrumentId, ComponentId = componentId });
        }

        public async Task<long> SaveTargetAsync(QcTargetDto target, long? userId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = @"
                INSERT INTO qc_targets (qc_material_id, instrument_id, component_id, mean, sd, unit, created_at, updated_at)
                VALUES (@QcMaterialId, @InstrumentId, @ComponentId, @Mean, @Sd, @Unit, clock_timestamp(), clock_timestamp())
                ON CONFLICT (qc_material_id, instrument_id, component_id) DO UPDATE
                SET mean = EXCLUDED.mean, sd = EXCLUDED.sd, unit = EXCLUDED.unit, updated_at = clock_timestamp()
                RETURNING id;
            ";
            return await conn.ExecuteScalarAsync<long>(sql, target);
        }

        public async Task<bool> DeleteTargetAsync(long id, long? userId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = "DELETE FROM qc_targets WHERE id = @Id;";
            int affected = await conn.ExecuteAsync(sql, new { Id = id });
            return affected > 0;
        }

        #endregion

        #region Results & Levey-Jennings

        public async Task<IEnumerable<QcResultDto>> GetResultsAsync(long instrumentId, long componentId, long? materialId = null, DateTime? fromDate = null, DateTime? toDate = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT r.id, r.instrument_id AS InstrumentId, ins.name AS InstrumentName,
                       r.qc_material_id AS QcMaterialId, m.name AS MaterialName, m.lot_no AS LotNo, m.level AS Level,
                       r.component_id AS ComponentId, c.display_code AS ComponentCode, c.name_en AS ComponentName,
                       r.value, r.received_at AS ReceivedAt, r.rule_violation AS RuleViolation,
                       r.accepted, r.corrective_action AS CorrectiveAction, r.user_id AS UserId, u.full_name AS UserName,
                       t.mean, t.sd
                FROM qc_results r
                JOIN instruments ins ON ins.id = r.instrument_id
                JOIN components c ON c.id = r.component_id
                LEFT JOIN qc_materials m ON m.id = r.qc_material_id
                LEFT JOIN qc_targets t ON t.qc_material_id = r.qc_material_id AND t.instrument_id = r.instrument_id AND t.component_id = r.component_id
                LEFT JOIN users u ON u.id = r.user_id
                WHERE r.instrument_id = @InstrumentId
                  AND r.component_id = @ComponentId
                  AND (@MaterialId IS NULL OR r.qc_material_id = @MaterialId)
                  AND (@FromDate IS NULL OR r.received_at >= @FromDate)
                  AND (@ToDate IS NULL OR r.received_at <= @ToDate)
                ORDER BY r.received_at ASC, r.id ASC;
            ";
            return await conn.QueryAsync<QcResultDto>(sql, new { 
                InstrumentId = instrumentId, 
                ComponentId = componentId, 
                MaterialId = materialId,
                FromDate = fromDate,
                ToDate = toDate
            });
        }

        public async Task<long> RecordResultAsync(QcResultDto result, long? userId = null)
        {
            using var conn = _connectionFactory.CreateConnection();

            // Try to find matching QC target if material is specified
            QcTargetDto? target = null;
            if (result.QcMaterialId.HasValue && result.QcMaterialId.Value > 0)
            {
                target = await GetTargetAsync(result.QcMaterialId.Value, result.InstrumentId, result.ComponentId);
            }

            if (target != null && target.Sd > 0)
            {
                result.Mean = target.Mean;
                result.Sd = target.Sd;

                // Load last 10 runs to evaluate Westgard rules
                string prevSql = @"
                    SELECT value
                    FROM qc_results
                    WHERE instrument_id = @InstrumentId AND component_id = @ComponentId AND qc_material_id = @MaterialId
                    ORDER BY received_at DESC, id DESC
                    LIMIT 10;
                ";
                var recentVals = (await conn.QueryAsync<decimal>(prevSql, new {
                    InstrumentId = result.InstrumentId,
                    ComponentId = result.ComponentId,
                    MaterialId = result.QcMaterialId
                })).ToList();

                var eval = EvaluateWestgard(result.Value, target.Mean, target.Sd, recentVals);
                result.RuleViolation = eval.RuleViolated;
                if (eval.IsRejection)
                {
                    result.Accepted = false;
                }
            }

            string insertSql = @"
                INSERT INTO qc_results (instrument_id, qc_material_id, component_id, value, received_at, rule_violation, accepted, corrective_action, user_id, created_at)
                VALUES (@InstrumentId, @QcMaterialId, @ComponentId, @Value, @ReceivedAt, @RuleViolation, @Accepted, @CorrectiveAction, @UserId, clock_timestamp())
                RETURNING id;
            ";
            result.UserId = userId ?? result.UserId;
            long id = await conn.ExecuteScalarAsync<long>(insertSql, result);
            result.Id = id;
            return id;
        }

        public async Task<bool> UpdateResultActionAsync(long resultId, bool accepted, string? correctiveAction, long? userId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = @"
                UPDATE qc_results
                SET accepted = @Accepted, corrective_action = @CorrectiveAction, user_id = COALESCE(@UserId, user_id)
                WHERE id = @Id;
            ";
            int affected = await conn.ExecuteAsync(sql, new { Id = resultId, Accepted = accepted, CorrectiveAction = correctiveAction, UserId = userId });
            return affected > 0;
        }

        #endregion

        #region Westgard Rules Evaluation

        public WestgardEvaluation EvaluateWestgard(decimal currentValue, decimal mean, decimal sd, IEnumerable<decimal> recentValues)
        {
            var res = new WestgardEvaluation();
            if (sd <= 0) return res;

            decimal z = (currentValue - mean) / sd;
            var history = recentValues?.ToList() ?? new List<decimal>();

            // Rule 1-3s: Rejection if 1 point exceeds 3 SD
            if (Math.Abs(z) > 3)
            {
                res.RuleViolated = "1-3s";
                res.IsRejection = true;
                res.Message = $"مخالفة قاعدة 1-3s (رفض قطعي): القيمة ({currentValue}) تجاوزت 3 انحرافات معيارية (Z-Score = {z:F2})";
                return res;
            }

            // Rule 2-2s: Rejection if 2 consecutive points exceed 2 SD on the same side
            if (history.Count >= 1)
            {
                decimal prevZ = (history[0] - mean) / sd;
                if (z > 2 && prevZ > 2)
                {
                    res.RuleViolated = "2-2s";
                    res.IsRejection = true;
                    res.Message = "مخالفة قاعدة 2-2s (رفض): نقطتان متتاليتان تتجاوزان +2 SD في نفس الاتجاه";
                    return res;
                }
                if (z < -2 && prevZ < -2)
                {
                    res.RuleViolated = "2-2s";
                    res.IsRejection = true;
                    res.Message = "مخالفة قاعدة 2-2s (رفض): نقطتان متتاليتان تتجاوزان -2 SD في نفس الاتجاه";
                    return res;
                }
            }

            // Rule R-4s: Rejection if difference between 2 consecutive points exceeds 4 SD (one > +2SD, one < -2SD)
            if (history.Count >= 1)
            {
                decimal prevZ = (history[0] - mean) / sd;
                if ((z >= 2 && prevZ <= -2) || (z <= -2 && prevZ >= 2))
                {
                    res.RuleViolated = "R-4s";
                    res.IsRejection = true;
                    res.Message = $"مخالفة قاعدة R-4s (رفض): النطاق بين نقطتين متتاليتين تجاوز 4 SD (Z1={z:F2}, Z0={prevZ:F2})";
                    return res;
                }
            }

            // Rule 4-1s: Rejection if 4 consecutive points exceed 1 SD on the same side
            if (history.Count >= 3)
            {
                decimal z1 = (history[0] - mean) / sd;
                decimal z2 = (history[1] - mean) / sd;
                decimal z3 = (history[2] - mean) / sd;

                if (z > 1 && z1 > 1 && z2 > 1 && z3 > 1)
                {
                    res.RuleViolated = "4-1s";
                    res.IsRejection = true;
                    res.Message = "مخالفة قاعدة 4-1s (رفض تحيز): 4 نقاط متتالية تتجاوز +1 SD";
                    return res;
                }
                if (z < -1 && z1 < -1 && z2 < -1 && z3 < -1)
                {
                    res.RuleViolated = "4-1s";
                    res.IsRejection = true;
                    res.Message = "مخالفة قاعدة 4-1s (رفض تحيز): 4 نقاط متتالية تتجاوز -1 SD";
                    return res;
                }
            }

            // Rule 10-x: Rejection / Shift if 10 consecutive points fall on the same side of the mean
            if (history.Count >= 9)
            {
                var allTen = new List<decimal> { currentValue };
                allTen.AddRange(history.Take(9));

                if (allTen.All(v => v > mean))
                {
                    res.RuleViolated = "10-x";
                    res.IsRejection = true;
                    res.Message = "مخالفة قاعدة 10-x (إزاحة منهجية): 10 نتائج متتالية تقع فوق المتوسط";
                    return res;
                }
                if (allTen.All(v => v < mean))
                {
                    res.RuleViolated = "10-x";
                    res.IsRejection = true;
                    res.Message = "مخالفة قاعدة 10-x (إزاحة منهجية): 10 نتائج متتالية تقع تحت المتوسط";
                    return res;
                }
            }

            // Rule 1-2s: Warning if 1 point exceeds 2 SD (random error warning)
            if (Math.Abs(z) > 2)
            {
                res.RuleViolated = "1-2s";
                res.IsWarning = true;
                res.Message = $"تنبيه قاعدة 1-2s (تحذير): القيمة ({currentValue}) تجاوزت 2 انحراف معياري (Z = {z:F2})";
                return res;
            }

            return res;
        }

        public async Task<TodayQcStatusDto> GetTodayQcStatusAsync(long instrumentId, long componentId)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT r.value, r.rule_violation AS RuleViolation, r.accepted, r.received_at AS ReceivedAt
                FROM qc_results r
                WHERE r.instrument_id = @InstrumentId
                  AND r.component_id = @ComponentId
                  AND r.received_at >= CURRENT_DATE
                ORDER BY r.received_at DESC, r.id DESC;
            ";
            var runs = (await conn.QueryAsync(sql, new { InstrumentId = instrumentId, ComponentId = componentId })).ToList();

            var status = new TodayQcStatusDto
            {
                InstrumentId = instrumentId,
                ComponentId = componentId,
                TotalRunsToday = runs.Count
            };

            if (runs.Count == 0)
            {
                status.Status = "NotRun";
                return status;
            }

            status.LastRunAt = (DateTime)runs[0].receivedat;
            string? violation = runs[0].ruleviolation;
            bool accepted = runs[0].accepted;

            status.LastViolation = violation;

            if (!accepted || violation is "1-3s" or "2-2s" or "R-4s" or "4-1s" or "10-x")
            {
                status.Status = "Failed";
            }
            else if (violation is "1-2s")
            {
                status.Status = "Warning";
            }
            else
            {
                status.Status = "Passed";
            }

            return status;
        }

        #endregion
    }
}

