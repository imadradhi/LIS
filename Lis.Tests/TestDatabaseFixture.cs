using System;
using System.IO;
using Lis.Core.Data;
using Microsoft.Extensions.Configuration;

namespace Lis.Tests
{
    public class TestDatabaseFixture
    {
        public IDbConnectionFactory ConnectionFactory { get; }
        public string CatalogPath { get; }

        public TestDatabaseFixture()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .Build();

            var connStr = config.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Port=5432;Database=lis_db;Username=postgres;Password=@Hig5udMz;";

            ConnectionFactory = new DbConnectionFactory(connStr);

            // Locate catalog directory
            var current = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(current) && !Directory.Exists(Path.Combine(current, "catalog")))
            {
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }

            CatalogPath = Directory.Exists(Path.Combine(current, "catalog"))
                ? Path.Combine(current, "catalog")
                : Path.GetFullPath(@"g:\Dev\LIS Interface\catalog");
        }
    }
}
