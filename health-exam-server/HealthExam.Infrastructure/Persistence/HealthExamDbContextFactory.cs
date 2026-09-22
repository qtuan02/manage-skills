namespace HealthExam.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class HealthExamDbContextFactory
    : IDesignTimeDbContextFactory<HealthExamDbContext>
{
    public HealthExamDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("HEALTHEXAM_DB") ?? "";
        var options = new DbContextOptionsBuilder<HealthExamDbContext>()
            .UseNpgsql(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        return new HealthExamDbContext(options);
    }
}
