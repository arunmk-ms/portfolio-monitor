using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PortfolioApp.Data;

/// <summary>
/// Lets `dotnet ef migrations` build a context without starting the Functions host.
/// Only used by EF tooling at design time, never at runtime.
/// </summary>
public sealed class PortfolioDbContextFactory : IDesignTimeDbContextFactory<PortfolioDbContext>
{
    public PortfolioDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__PortfolioDb")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=portfoliomonitor;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new PortfolioDbContext(options);
    }
}
