using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Data;
using Lis.Core.Models;

namespace Lis.Core.Services
{
    public class PatientAgeInfo
    {
        public int Years { get; set; }
        public int Months { get; set; }
        public int Days { get; set; }
        public int TotalDays { get; set; }

        public string FormattedText
        {
            get
            {
                if (Years >= 1) return $"{Years} سنة";
                if (Months >= 1) return $"{Months} شهر";
                return $"{Days} يوم";
            }
        }
    }

    public interface IPatientService
    {
        Task<IEnumerable<Patient>> SearchPatientsAsync(string query, int limit = 50);
        Task<Patient?> GetPatientByIdAsync(long id);
        Task<Patient?> GetPatientByMrnAsync(long mrn);
        Task<Patient> CreatePatientAsync(Patient patient, long? currentUserId = null);
        Task UpdatePatientAsync(Patient patient, long? currentUserId = null);
        Task<List<Patient>> CheckDuplicatesAsync(string fullNameAr, DateTime dateOfBirth, long? excludePatientId = null);
        PatientAgeInfo CalculateAge(DateTime dateOfBirth, DateTime? referenceDate = null);
    }

    public class PatientService : IPatientService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IAuthService _authService;

        public PatientService(IDbConnectionFactory connectionFactory, IAuthService authService)
        {
            _connectionFactory = connectionFactory;
            _authService = authService;
        }

        public async Task<IEnumerable<Patient>> SearchPatientsAsync(string query, int limit = 50)
        {
            using var conn = _connectionFactory.CreateConnection();
            string trimmed = (query ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return await conn.QueryAsync<Patient>(@"
                    SELECT id, mrn, full_name_ar AS FullNameAr, full_name_en AS FullNameEn, sex, 
                           date_of_birth AS DateOfBirth, phone, national_id AS NationalId, notes,
                           created_by AS CreatedBy, created_at AS CreatedAt, updated_at AS UpdatedAt
                    FROM patients
                    ORDER BY id DESC
                    LIMIT @Limit;
                ", new { Limit = limit });
            }

            // If query is a number, match MRN or phone
            bool isNumber = long.TryParse(trimmed, out var numericVal);

            const string sql = @"
                SELECT id, mrn, full_name_ar AS FullNameAr, full_name_en AS FullNameEn, sex, 
                       date_of_birth AS DateOfBirth, phone, national_id AS NationalId, notes,
                       created_by AS CreatedBy, created_at AS CreatedAt, updated_at AS UpdatedAt
                FROM patients
                WHERE (@IsNumber = TRUE AND mrn = @NumericVal)
                   OR phone ILIKE '%' || @Query || '%'
                   OR full_name_ar ILIKE '%' || @Query || '%'
                   OR full_name_en ILIKE '%' || @Query || '%'
                ORDER BY id DESC
                LIMIT @Limit;
            ";

            return await conn.QueryAsync<Patient>(sql, new
            {
                IsNumber = isNumber,
                NumericVal = numericVal,
                Query = trimmed,
                Limit = limit
            });
        }

        public async Task<Patient?> GetPatientByIdAsync(long id)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT id, mrn, full_name_ar AS FullNameAr, full_name_en AS FullNameEn, sex, 
                       date_of_birth AS DateOfBirth, phone, national_id AS NationalId, notes,
                       created_by AS CreatedBy, created_at AS CreatedAt, updated_at AS UpdatedAt
                FROM patients
                WHERE id = @Id;
            ";
            return await conn.QueryFirstOrDefaultAsync<Patient>(sql, new { Id = id });
        }

        public async Task<Patient?> GetPatientByMrnAsync(long mrn)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT id, mrn, full_name_ar AS FullNameAr, full_name_en AS FullNameEn, sex, 
                       date_of_birth AS DateOfBirth, phone, national_id AS NationalId, notes,
                       created_by AS CreatedBy, created_at AS CreatedAt, updated_at AS UpdatedAt
                FROM patients
                WHERE mrn = @Mrn;
            ";
            return await conn.QueryFirstOrDefaultAsync<Patient>(sql, new { Mrn = mrn });
        }

        public async Task<Patient> CreatePatientAsync(Patient patient, long? currentUserId = null)
        {
            using var conn = _connectionFactory.CreateConnection();

            const string sql = @"
                INSERT INTO patients (full_name_ar, full_name_en, sex, date_of_birth, phone, national_id, notes, created_by, updated_at)
                VALUES (@FullNameAr, @FullNameEn, @Sex, @DateOfBirth, @Phone, @NationalId, @Notes, @CreatedBy, clock_timestamp())
                RETURNING id, mrn, full_name_ar AS FullNameAr, full_name_en AS FullNameEn, sex, 
                          date_of_birth AS DateOfBirth, phone, national_id AS NationalId, notes,
                          created_by AS CreatedBy, created_at AS CreatedAt, updated_at AS UpdatedAt;
            ";

            var created = await conn.QuerySingleAsync<Patient>(sql, new
            {
                FullNameAr = patient.FullNameAr.Trim(),
                FullNameEn = patient.FullNameEn?.Trim(),
                Sex = patient.Sex,
                DateOfBirth = patient.DateOfBirth.Date,
                Phone = patient.Phone?.Trim(),
                NationalId = patient.NationalId?.Trim(),
                Notes = patient.Notes?.Trim(),
                CreatedBy = currentUserId
            });

            await _authService.LogAuditAsync(currentUserId, "CREATE_PATIENT", "patients", created.Id.ToString(), null, created);

            return created;
        }

        public async Task UpdatePatientAsync(Patient patient, long? currentUserId = null)
        {
            var old = await GetPatientByIdAsync(patient.Id);
            if (old == null) throw new InvalidOperationException($"Patient {patient.Id} not found");

            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                UPDATE patients
                SET full_name_ar = @FullNameAr,
                    full_name_en = @FullNameEn,
                    sex = @Sex,
                    date_of_birth = @DateOfBirth,
                    phone = @Phone,
                    national_id = @NationalId,
                    notes = @Notes,
                    updated_at = clock_timestamp()
                WHERE id = @Id;
            ";

            await conn.ExecuteAsync(sql, new
            {
                Id = patient.Id,
                FullNameAr = patient.FullNameAr.Trim(),
                FullNameEn = patient.FullNameEn?.Trim(),
                Sex = patient.Sex,
                DateOfBirth = patient.DateOfBirth.Date,
                Phone = patient.Phone?.Trim(),
                NationalId = patient.NationalId?.Trim(),
                Notes = patient.Notes?.Trim()
            });

            await _authService.LogAuditAsync(currentUserId, "UPDATE_PATIENT", "patients", patient.Id.ToString(), old, patient);
        }

        public async Task<List<Patient>> CheckDuplicatesAsync(string fullNameAr, DateTime dateOfBirth, long? excludePatientId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                SELECT id, mrn, full_name_ar AS FullNameAr, full_name_en AS FullNameEn, sex, 
                       date_of_birth AS DateOfBirth, phone, national_id AS NationalId
                FROM patients
                WHERE full_name_ar = @FullNameAr 
                  AND date_of_birth = @DateOfBirth
                  AND (@ExcludeId IS NULL OR id != @ExcludeId);
            ";

            var result = await conn.QueryAsync<Patient>(sql, new
            {
                FullNameAr = fullNameAr.Trim(),
                DateOfBirth = dateOfBirth.Date,
                ExcludeId = excludePatientId
            });

            return result.AsList();
        }

        public PatientAgeInfo CalculateAge(DateTime dateOfBirth, DateTime? referenceDate = null)
        {
            var target = (referenceDate ?? DateTime.Today).Date;
            var birth = dateOfBirth.Date;

            if (target < birth)
            {
                return new PatientAgeInfo { Years = 0, Months = 0, Days = 0, TotalDays = 0 };
            }

            int years = target.Year - birth.Year;
            int months = target.Month - birth.Month;
            int days = target.Day - birth.Day;

            if (days < 0)
            {
                months--;
                var prevMonthDays = DateTime.DaysInMonth(target.Year, target.Month == 1 ? 12 : target.Month - 1);
                days += prevMonthDays;
            }

            if (months < 0)
            {
                years--;
                months += 12;
            }

            int totalDays = (int)(target - birth).TotalDays;

            return new PatientAgeInfo
            {
                Years = years,
                Months = months,
                Days = days,
                TotalDays = totalDays
            };
        }
    }
}
