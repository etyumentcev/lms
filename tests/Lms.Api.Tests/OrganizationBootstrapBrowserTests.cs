using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Lms.Api.Tests;

public sealed class OrganizationBootstrapBrowserTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private sealed class BrowserClock : TimeProvider, IQualifiedClockSource
    {
        private readonly DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public QualifiedClockReading Read() => new("m1-03-browser", true,
            new(now, now), TimeSpan.Zero, TimeSpan.Zero);
    }

    [BrowserReadTheory]
    [InlineData(true)]
    public async Task Real_browser_creates_activates_and_opens_organization_admin_dashboard(bool browser)
    {
        Assert.True(browser);
        await using var database = await fixture.CreateApp();
        await using var databaseContext = database.Open();
        var connectionString = databaseContext.Database.GetConnectionString()!;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var web = Path.Combine(root.FullName, "src/Lms.Web/dist");
        Assert.True(File.Exists(Path.Combine(web, "index.html")), "Build the production React UI first");

        using var tls = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", tls, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin = $"https://127.0.0.1:{port}";

        var time = new BrowserClock();
        var clock = new QualifiedAuthorityClock(time, TimeSpan.Zero, TimeSpan.Zero);
        using var componentKey = RSA.Create(2048);
        var jwt = new ComponentJwt(componentKey, "m1-03-browser", "m1-03-browser", clock);
        var platformKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.LocalSigningKey));
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port,
            listen => listen.UseHttps(certificate)));
        builder.Services.AddSingleton(time); builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton(clock); builder.Services.AddSingleton<IAuthorityClock>(clock);
        builder.Services.AddSingleton(jwt);
        builder.Services.AddDbContextFactory<LmsDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<LmsDbContext>>().CreateDbContext());
        builder.Services.AddSingleton(new OrganizationBootstrapOptions
        {
            ChallengeKey = Enumerable.Range(11, 32).Select(x => (byte)x).ToArray(), KeyVersion = 1
        });
        builder.Services.AddScoped<IOrganizationBootstrap, PostgresOrganizationBootstrap>();
        builder.Services.AddScoped<IOrganizationAdministrationQueries, PostgresOrganizationAdministrationQueries>();
        builder.Services.AddScoped<ICourseRosterAdministration, PostgresCourseRosterAdministration>();
        builder.Services.AddScoped<PostgresTeacherCommands>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddTeacherRevocationBrowserProtection();
        builder.Services.AddRateLimiter(options => options.AddPolicy(OrganizationBootstrapHttp.ActivationRatePolicy,
            context => RateLimitPartition.GetFixedWindowLimiter("browser", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        builder.Services.AddAuthentication(CourseBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CourseBearerHandler>(CourseBearerHandler.SchemeName, _ => { })
            .AddJwtBearer(OrganizationBootstrapHttp.PlatformBearerScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new()
                {
                    ValidIssuer = "m1-03-platform", ValidAudience = OrganizationBootstrapHttp.PlatformAudience,
                    IssuerSigningKey = platformKey, ValidateIssuer = true, ValidateAudience = true,
                    ValidateIssuerSigningKey = true, ValidateLifetime = true, RequireSignedTokens = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        builder.Services.AddAuthorization();

        await using var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>(); app.UseMiddleware<CourseRequestIngress>();
        app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
        app.MapTeacherRevocation(origin); app.MapOrganizationBootstrap(origin); app.MapCourseRoster(origin);
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(web) });
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = 404; return; }
            context.Response.ContentType = "text/html; charset=utf-8";
            await Microsoft.AspNetCore.Http.SendFileResponseExtensions.SendFileAsync(context.Response,
                Path.Combine(web, "index.html"));
        });
        await app.StartAsync();

        var now = DateTime.UtcNow;
        var platformToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "m1-03-platform", OrganizationBootstrapHttp.PlatformAudience,
            [new Claim("sub", "synthetic-platform-operator"),
             new Claim("scope", OrganizationBootstrapHttp.BootstrapCapability)],
            now, now.AddMinutes(10), new SigningCredentials(platformKey, SecurityAlgorithms.HmacSha256)));
        var evidence = Path.Combine(root.FullName, "codex/evidence/M1-03-ORGANIZATION-BOOTSTRAP");
        Directory.CreateDirectory(evidence);
        var node = Environment.GetEnvironmentVariable("LMS_BROWSER_NODE") ?? "node";
        var start = new ProcessStartInfo(node)
        {
            WorkingDirectory = root.FullName, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        var browserExecutable = Environment.GetEnvironmentVariable("LMS_BROWSER_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(browserExecutable) && File.Exists("/usr/bin/google-chrome"))
            browserExecutable = "/usr/bin/google-chrome";
        if (!string.IsNullOrWhiteSpace(browserExecutable))
            start.Environment["LMS_BROWSER_EXECUTABLE"] = browserExecutable;
        start.ArgumentList.Add(Path.Combine(root.FullName, "tools/browser_read/organization-bootstrap.mjs"));
        using var runner = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var errors = runner.StandardError.ReadToEndAsync(timeout.Token);
        const string password = "Synthetic-Browser-Admin-2039!";
        var userName = "browser-admin-" + Guid.NewGuid().ToString("N")[..8];
        await runner.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            origin, platformToken, organizationName = "Синтетическая гимназия", adminUserName = userName,
            password, evidence
        }));
        var passed = await runner.StandardOutput.ReadLineAsync(timeout.Token) == "PASS";
        await runner.WaitForExitAsync(timeout.Token);
        var diagnostic = (await errors).Replace(platformToken, "[REDACTED]")
            .Replace(password, "[REDACTED]").Replace(userName, "[REDACTED]");
        Assert.True(passed && runner.ExitCode == 0, diagnostic);

        await using var verify = database.Open();
        Assert.Single(await verify.Set<OrganizationBootstrapReceipt>().ToListAsync());
        Assert.Single(await verify.Set<ActorIdentity>().Where(x => x.IsActive).ToListAsync());
        Assert.Empty(await verify.Set<EnrollmentScope>().ToListAsync());
        Assert.Empty(await verify.Set<TeacherAssignmentScope>().ToListAsync());
    }
}
