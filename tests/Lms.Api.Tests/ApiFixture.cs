using System.Data.Common;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Lms.Api.Tests;

public sealed class ApiPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17.6-alpine").Build();
    public Task InitializeAsync() => container.StartAsync();
    public Task DisposeAsync() => container.DisposeAsync().AsTask();
    public async Task<ApiFactory> CreateApp(bool testAuthentication = true)
    {
        var name = "c2_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        await command.ExecuteNonQueryAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name }.ConnectionString;
        var factory = new ApiFactory(connectionString, testAuthentication);
        await using var db = factory.Open();
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        return factory;
    }
}

public sealed class ApiFactory(string connectionString, bool testAuthentication) : WebApplicationFactory<Program>
{
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "lms-u1-test-" + Guid.NewGuid().ToString("N"));
    public ApiTransactionBarrier Barrier { get; } = new();
    public static readonly Guid Student = Guid.NewGuid();
    public static readonly Guid Teacher = Guid.NewGuid();
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Lms", connectionString);
        builder.UseSetting("Attachments:Root", StorageRoot);
        builder.UseSetting("Attachments:MaxBytes", "1024");
        builder.ConfigureTestServices(services => services.ConfigureDbContext<LmsDbContext>(options => options.AddInterceptors(Barrier)));
        if (testAuthentication)
            builder.ConfigureTestServices(services => services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { }));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, recursive: true);
    }

    public LmsDbContext Open() => new(new DbContextOptionsBuilder<LmsDbContext>().UseNpgsql(connectionString).Options);
    public HttpClient Client(Guid? id = null, string role = "Student")
    {
        var client = CreateClient();
        if (id.HasValue)
        {
            client.DefaultRequestHeaders.Add("X-Test-Subject", id.ToString());
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
        }
        return client;
    }

    public async Task<ProblemAssignment> Seed(bool active = true, bool published = true, bool enrolled = true,
        string? teacherGroup = null, string? studentGroup = null, bool teacherGrant = true,
        DateTimeOffset? availableFrom = null)
    {
        var assignment = new ProblemAssignment(Guid.NewGuid(), Student, Guid.NewGuid().ToString(),
            new ProblemContentReference(Guid.NewGuid(), "lesson", "problem"));
        if (!active) assignment.Withdraw();
        await using var db = Open();
        db.Assignments.Add(assignment);
        db.Enrollments.Add(new EnrollmentAccess { StudentId = Student, CourseId = assignment.CourseId, IsActive = enrolled, GroupId = studentGroup });
        db.Lessons.Add(new LessonAccess { CourseId = assignment.CourseId, CourseVersionId = assignment.Content.CourseVersionId,
            LessonId = "lesson", IsPublished = published, AvailableFrom = availableFrom });
        if (teacherGrant) db.TeacherGrants.Add(new TeacherAccess { Id = Guid.NewGuid(), TeacherId = Teacher,
            CourseId = assignment.CourseId, GroupId = teacherGroup });
        await db.SaveChangesAsync();
        return assignment;
    }

    public async Task<Guid> Attachment(Guid owner, bool eligible = true, bool expired = false)
    {
        // Every eligible fixture now has real, validated immutable binary storage.
        using var client = Client(owner);
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(AttachmentImages.Png);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "fixture.png");
        var response = await client.PostAsync("/api/v1/uploads", form);
        response.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("attachmentId").GetGuid();
        await using var db = Open();
        var metadata = await db.AttachmentMetadata.SingleAsync(x => x.Id == id);
        metadata.IsEligible = eligible;
        metadata.ExpiresAt = DateTimeOffset.UtcNow.AddHours(expired ? -1 : 1);
        await db.SaveChangesAsync();
        return id;
    }

    public async Task<ProblemWork> Reload(Guid workId)
    {
        await using var db = Open();
        return (await new PostgresWorkStore(db).FindWork(workId, default))!;
    }
}

// Deliberately exists only in the test assembly; never enabled by an environment/config flag in production.
public sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Subject", out var subject))
            return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new("sub", subject.ToString()) };
        claims.AddRange(Request.Headers["X-Test-Role"].ToString().Split(',').Select(role => new Claim("role", role)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}

// Forces both HTTP requests into independent real DB transactions before either can save.
public sealed class ApiTransactionBarrier : DbTransactionInterceptor
{
    private int arrivals;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Armed { get; set; }
    public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
        TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        if (Armed && result.IsolationLevel != System.Data.IsolationLevel.RepeatableRead)
        {
            if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        return result;
    }
}
