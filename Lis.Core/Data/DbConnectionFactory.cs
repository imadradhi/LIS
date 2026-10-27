using System;
using System.Data;
using Npgsql;
using Microsoft.Extensions.Configuration;

namespace Lis.Core.Data
{
    public interface IDbConnectionFactory
    {
        IDbConnection CreateConnection();
        string ConnectionString { get; }
    }

    public class DbConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public DbConnectionFactory(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be empty", nameof(connectionString));

            _connectionString = connectionString;
        }

        public DbConnectionFactory(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("DefaultConnection connection string is not configured in appsettings.json.");
        }

        public string ConnectionString => _connectionString;

        public IDbConnection CreateConnection()
        {
            var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            return conn;
        }
    }
}
