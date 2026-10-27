using System;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Data;

namespace Lis.Core.Services
{
    public interface IAccessionService
    {
        Task<string> GenerateNextAccessionAsync(IDbConnection? connection = null, IDbTransaction? transaction = null);
        bool IsValidAccession(string accession);
    }

    public class AccessionService : IAccessionService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private static readonly Regex AccessionRegex = new Regex(@"^[0-9]{6}$", RegexOptions.Compiled);

        public AccessionService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public bool IsValidAccession(string accession)
        {
            return !string.IsNullOrWhiteSpace(accession) && AccessionRegex.IsMatch(accession);
        }

        public async Task<string> GenerateNextAccessionAsync(IDbConnection? connection = null, IDbTransaction? transaction = null)
        {
            bool localConn = false;
            IDbConnection conn = connection!;

            if (conn == null)
            {
                conn = _connectionFactory.CreateConnection();
                localConn = true;
            }

            try
            {
                // Calls the PostgreSQL wraparound-safe function created in schema: generate_next_accession()
                const string sql = "SELECT generate_next_accession();";
                var accession = await conn.ExecuteScalarAsync<string>(sql, transaction: transaction);

                if (string.IsNullOrWhiteSpace(accession) || !IsValidAccession(accession))
                {
                    throw new InvalidOperationException($"Generated accession '{accession}' is invalid or empty.");
                }

                return accession;
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
