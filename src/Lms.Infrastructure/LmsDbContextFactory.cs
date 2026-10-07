using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lms.Infrastructure;

public sealed class LmsDbContextFactory : IDesignTimeDbContextFactory<LmsDbContext>
{
    public LmsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<LmsDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("LMS_POSTGRES")
            ?? "Host=localhost;Database=lms;Username=postgres;Password=postgres").Options);
}
