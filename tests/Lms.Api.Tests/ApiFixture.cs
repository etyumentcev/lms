using System.Data.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Identity;
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
    public async Task<ApiFactory> CreateApp(bool testAuthentication = true, bool organizationBootstrap = false)
    {
        var name = "c2_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        await command.ExecuteNonQueryAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name }.ConnectionString;
        var factory = new ApiFactory(connectionString, testAuthentication, organizationBootstrap);
        await using var db = factory.Open();
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        return factory;
    }
}

public sealed class ApiFactory(string connectionString, bool testAuthentication, bool organizationBootstrap = false) : WebApplicationFactory<Program>
{
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "lms-u1-test-" + Guid.NewGuid().ToString("N"));
    public string SigningKeyPath { get; } = Path.Combine(Path.GetTempPath(), "lms-teacher-key-" + Guid.NewGuid().ToString("N") + ".pem");
    public ApiTransactionBarrier Barrier { get; } = new();
    public static readonly Guid Student = Guid.NewGuid();
    public static readonly Guid Teacher = Guid.NewGuid();
    public Guid OrganizationId { get; } = Guid.NewGuid();
    public Guid ProcessingContextId { get; } = Guid.NewGuid();
    public string TeacherUserName { get; } = "teacher-" + Guid.NewGuid().ToString("N");
    public string StudentUserName { get; } = "student-" + Guid.NewGuid().ToString("N");
    public const string SecurePassword = "Synthetic-password-123";
    public const string LocalSigningKey = "synthetic-local-signing-key-for-M1-03-tests-0123456789";
    private readonly SemaphoreSlim securityInitialization = new(1, 1);
    private bool securityInitialized;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var repositoryRoot = FindRepositoryRoot();
        if (!File.Exists(SigningKeyPath))
        {
            using var key = RSA.Create(2048);
            File.WriteAllText(SigningKeyPath, key.ExportRSAPrivateKeyPem());
        }
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Lms", connectionString);
        builder.UseSetting("Attachments:Root", StorageRoot);
        builder.UseSetting("Attachments:MaxBytes", "1024");
        builder.UseSetting("TeacherRevocation:Enabled", testAuthentication.ToString());
        builder.UseSetting("TeacherRevocation:AllowedOrigin", "https://lms.test");
        builder.UseSetting("TeacherRevocation:SigningKeyPath", SigningKeyPath);
        builder.UseSetting("TeacherRevocation:Issuer", "lms-api-tests");
        builder.UseSetting("TeacherRevocation:Audience", "lms-api-tests");
        builder.UseSetting("TeacherRevocation:SyntheticDevelopmentClock", "true");
        builder.UseSetting("TeacherRevocation:RestrictedDatabaseCapabilities", "false");
        builder.UseSetting("ContentImport:RepositoryRoot", repositoryRoot);
        builder.UseSetting("ContentImport:PythonPath", "/usr/bin/python3");
        builder.UseSetting("ContentImport:NodePath",
            "/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node");
        builder.UseSetting("ContentImport:KatexPath", Path.Combine(repositoryRoot, "src", "Lms.Web", "node_modules", "katex"));
        builder.UseSetting("ContentImport:StorageRoot", Path.Combine(StorageRoot, "content-import"));
        builder.UseSetting("ContentImport:MaxZipBytes", "20000000");
        if (organizationBootstrap)
        {
            builder.UseSetting("OrganizationBootstrap:Enabled", "true");
            builder.UseSetting("OrganizationBootstrap:ChallengeKey",
                Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray()));
            builder.UseSetting("Authentication:PlatformAudience", OrganizationBootstrapHttp.PlatformAudience);
            builder.UseSetting("LocalDemo:Enabled", "true");
            builder.UseSetting("LocalDemo:SigningKey", LocalSigningKey);
        }
        builder.ConfigureTestServices(services => services.ConfigureDbContext<LmsDbContext>(options => options.AddInterceptors(Barrier)));
        if (testAuthentication)
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                });
            });
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Lms.sln")))
                directory = directory.Parent;
            if (directory != null) return directory.FullName;
        }
        throw new InvalidOperationException("Lms.sln repository root was not found.");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, recursive: true);
        if (File.Exists(SigningKeyPath)) File.Delete(SigningKeyPath);
    }

    public LmsDbContext Open() => new(new DbContextOptionsBuilder<LmsDbContext>().UseNpgsql(connectionString).Options);
    public async Task InitializeTeacherSecurity()
    {
        await securityInitialization.WaitAsync();
        try
        {
        if (securityInitialized) return;
        var at = DateTimeOffset.UtcNow;
        await using (var db = Open())
        {
            db.Add(new Organization { OrganizationId = OrganizationId, State = "Enabled", SyntheticOnly = true });
            db.AddRange(new ActorRecord { ActorId = Teacher }, new ActorRecord { ActorId = Student });
            db.Add(new CourseRegistration { CourseId = "security-bootstrap", OrganizationId = OrganizationId, MappingState = "Verified" });
            db.Add(new CourseGroup { CourseId = "security-bootstrap", GroupId = "bootstrap" });
            await db.SaveChangesAsync();
        }
        await using (var db = Open())
        {
            var admin = new PostgresScopedAuthorityAdministration(db);
            await admin.Change(new(AuthorityKind.Membership, AuthorityOperation.Grant, OrganizationId, Teacher, 0, Role: ScopeRole.Teacher));
            await admin.Change(new(AuthorityKind.Membership, AuthorityOperation.Grant, OrganizationId, Student, 0, Role: ScopeRole.Student));
            await admin.Change(new(AuthorityKind.Teacher, AuthorityOperation.Grant, OrganizationId, Teacher, 0, 1,
                "security-bootstrap", "bootstrap", Guid.NewGuid()));
            await admin.Change(new(AuthorityKind.Enrollment, AuthorityOperation.Grant, OrganizationId, Student, 0, 1,
                "security-bootstrap", "bootstrap"));
        }
        await using (var db = Open())
            await new PostgresProcessingStateAdministration(db).Change(new(OrganizationId, Guid.NewGuid(), 0,
                new AddProcessingContext(ProcessingContextId, 1, at.AddMinutes(-1))));
        await CreateIdentity(Teacher, TeacherUserName);
        await CreateIdentity(Student, StudentUserName);
        securityInitialized = true;
        }
        finally { securityInitialization.Release(); }
    }
    private async Task CreateIdentity(Guid actor, string userName)
    {
        var userId = Guid.NewGuid();
        await using (var db = Open())
            await new PostgresIdentityMapping(db, TimeProvider.System).CreateInactive(userId, actor, OrganizationId, userName);
        await using (var db = Open())
        {
            (await db.Set<ActorIdentity>().SingleAsync(x => x.UserId == userId)).IsActive = true;
            var user = await db.Users.SingleAsync(x => x.Id == userId);
            user.PasswordHash = new PasswordHasher<IdentityUser<Guid>>().HashPassword(user, SecurePassword);
            await db.SaveChangesAsync();
        }
    }
    public HttpClient Client(Guid? id = null, string role = "Student")
    {
        var client = CreateClient(); client.BaseAddress = new("https://lms.test");
        if (id.HasValue)
        {
            client.DefaultRequestHeaders.Add("X-Test-Subject", id.ToString());
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
        }
        return client;
    }
    public async Task<HttpClient> SecureClient(string role = "Teacher")
    {
        await InitializeTeacherSecurity();
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://lms.test") });
        client.DefaultRequestHeaders.Add("Origin", "https://lms.test");
        async Task BindCsrf()
        {
            using var response = await client.GetAsync("/api/v1/auth/csrf");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", json.GetProperty("token").GetString());
        }
        await BindCsrf();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new {
            userName = role == "Teacher" ? TeacherUserName : StudentUserName,
            password = SecurePassword, organizationId = OrganizationId
        });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Existing read/student controllers still use the test scheme in this factory;
        // protected teacher endpoints explicitly authenticate CourseBearer.
        client.DefaultRequestHeaders.Add("X-Test-Subject", role == "Teacher" ? Teacher.ToString() : Student.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        await BindCsrf();
        return client;
    }

    public async Task<ProblemAssignment> Seed(bool active = true, bool published = true, bool enrolled = true,
        string? teacherGroup = null, string? studentGroup = null, bool teacherGrant = true,
        DateTimeOffset? availableFrom = null, bool scopedTeacher = false)
    {
        var assignment = new ProblemAssignment(Guid.NewGuid(), Student, Guid.NewGuid().ToString(),
            new ProblemContentReference(Guid.NewGuid(), "lesson", "problem"));
        if (!active) assignment.Withdraw();
        if (scopedTeacher)
        {
            await InitializeTeacherSecurity();
            await using (var setup = Open())
            {
                setup.Add(new CourseRegistration { CourseId = assignment.CourseId, OrganizationId = OrganizationId, MappingState = "Verified" });
                foreach (var group in new[] { teacherGroup, studentGroup }.Where(x => x != null).Distinct())
                    setup.Add(new CourseGroup { CourseId = assignment.CourseId, GroupId = group! });
                await setup.SaveChangesAsync();
                setup.Add(new CourseContextBinding { CourseId = assignment.CourseId, OrganizationId = OrganizationId,
                    ContextId = ProcessingContextId, ContextVersion = 1, EffectiveFrom = DateTimeOffset.UtcNow.AddMinutes(-1) });
                await setup.SaveChangesAsync();
            }
            await using (var scopes = Open())
            {
                var admin = new PostgresScopedAuthorityAdministration(scopes);
                await admin.Change(new(AuthorityKind.Enrollment, AuthorityOperation.Grant, OrganizationId, Student, 0, 1,
                    assignment.CourseId, studentGroup));
                if (teacherGrant) await admin.Change(new(AuthorityKind.Teacher, AuthorityOperation.Grant, OrganizationId, Teacher, 0, 1,
                    assignment.CourseId, teacherGroup, Guid.NewGuid()));
            }
        }
        await using var db = Open();
        db.Assignments.Add(assignment);
        if (!scopedTeacher)
            db.Enrollments.Add(new EnrollmentAccess { StudentId = Student, CourseId = assignment.CourseId, IsActive = enrolled, GroupId = studentGroup });
        db.Lessons.Add(new LessonAccess { CourseId = assignment.CourseId, CourseVersionId = assignment.Content.CourseVersionId,
            LessonId = "lesson", IsPublished = published, AvailableFrom = availableFrom });
        if (!scopedTeacher && teacherGrant) db.TeacherGrants.Add(new TeacherAccess { Id = Guid.NewGuid(), TeacherId = Teacher,
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
