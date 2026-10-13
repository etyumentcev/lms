using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

namespace Lms.Api;

internal sealed record TeacherRevocationHostOptions(bool Enabled, string AllowedOrigin,
    bool RestrictedDatabaseCapabilities, bool OrganizationBootstrapEnabled);

internal static class TeacherRevocationComposition
{
    internal const string HybridScheme = "LmsApiAuthentication";

    internal static TeacherRevocationHostOptions AddTeacherRevocationHost(this WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue<bool>("TeacherRevocation:Enabled"))
            return new(false, string.Empty, false, false);

        var origin = builder.Configuration["TeacherRevocation:AllowedOrigin"];
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var parsed) || parsed.Scheme != "https" ||
            parsed.GetLeftPart(UriPartial.Authority) != origin)
            throw new InvalidOperationException("TeacherRevocation:AllowedOrigin must be one exact HTTPS origin.");

        builder.Services.AddScoped<IDbContextFactory<LmsDbContext>, RequestDbContextFactory>();
        builder.Services.AddScoped<IProgressHistoryQueries, PostgresProgressHistoryQueries>();
        builder.Services.AddScoped<PostgresTeacherCommands>();
        builder.Services.AddDataProtection();
        builder.Services.AddTeacherRevocationBrowserProtection();

        var bootstrapEnabled = builder.Configuration.GetValue<bool>("OrganizationBootstrap:Enabled");
        if (bootstrapEnabled)
        {
            var encodedKey = builder.Configuration["OrganizationBootstrap:ChallengeKey"];
            byte[] challengeKey;
            try { challengeKey = Convert.FromBase64String(encodedKey ?? string.Empty); }
            catch (FormatException) { throw new InvalidOperationException("OrganizationBootstrap:ChallengeKey must be base64."); }
            var bootstrapOptions = new OrganizationBootstrapOptions { ChallengeKey = challengeKey,
                KeyVersion = builder.Configuration.GetValue<int?>("OrganizationBootstrap:ChallengeKeyVersion") ?? 1 };
            bootstrapOptions.Validate();
            builder.Services.AddSingleton(bootstrapOptions);
            builder.Services.AddScoped<IOrganizationBootstrap, PostgresOrganizationBootstrap>();
            builder.Services.AddScoped<IOrganizationAdministrationQueries, PostgresOrganizationAdministrationQueries>();
            builder.Services.AddScoped<PostgresAccountAdministration>();
            builder.Services.AddScoped<IAccountAdministration>(sp => sp.GetRequiredService<PostgresAccountAdministration>());
            builder.Services.AddScoped<IAccountSecurityOperations>(sp => sp.GetRequiredService<PostgresAccountAdministration>());
            builder.Services.AddScoped<ICourseRosterAdministration, PostgresCourseRosterAdministration>();
            var repositoryRoot = builder.Configuration["ContentImport:RepositoryRoot"] ?? string.Empty;
            var importOptions = new ContentImportRuntimeOptions
            {
                RepositoryRoot = repositoryRoot,
                PythonPath = builder.Configuration["ContentImport:PythonPath"] ?? "/usr/bin/python3",
                NodePath = builder.Configuration["ContentImport:NodePath"] ?? string.Empty,
                KatexPath = builder.Configuration["ContentImport:KatexPath"] ?? Path.Combine(repositoryRoot, "src", "Lms.Web", "node_modules", "katex"),
                StorageRoot = builder.Configuration["ContentImport:StorageRoot"] ?? string.Empty,
                MaxZipBytes = builder.Configuration.GetValue<long?>("ContentImport:MaxZipBytes") ?? 20_000_000,
            };
            builder.Services.AddSingleton(importOptions);
            builder.Services.AddScoped<IContentPublication, PostgresContentPublication>();
            builder.Services.AddRateLimiter(options => options.AddPolicy(OrganizationBootstrapHttp.ActivationRatePolicy,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                    { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
            builder.Services.AddAuthentication().AddJwtBearer(OrganizationBootstrapHttp.PlatformBearerScheme, options =>
            {
                options.Authority = builder.Configuration["Authentication:Authority"];
                options.Audience = builder.Configuration["Authentication:PlatformAudience"] ?? OrganizationBootstrapHttp.PlatformAudience;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidateLifetime = true;
                options.TokenValidationParameters.RequireSignedTokens = true;
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
                if (LocalDevelopment.Enabled(builder))
                {
                    options.Authority = null;
                    options.TokenValidationParameters.ValidIssuer = LocalDevelopment.Issuer;
                    options.TokenValidationParameters.ValidAudience = options.Audience;
                    options.TokenValidationParameters.IssuerSigningKey = LocalDevelopment.Key(builder.Configuration);
                }
            });
        }

        if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("TeacherRevocation:SyntheticDevelopmentClock"))
            builder.Services.AddSingleton<IQualifiedClockSource, SyntheticDevelopmentClockSource>();
        else
            builder.Services.AddSingleton<IQualifiedClockSource>(_ =>
                new LocalQualifiedClockSource(new LinuxLocalClockProbe(), new UnprovenLocalClockVerifier()));
        builder.Services.AddSingleton(sp => new QualifiedAuthorityClock(
            sp.GetRequiredService<IQualifiedClockSource>(), TimeSpan.Zero, TimeSpan.Zero));
        builder.Services.AddSingleton<IAuthorityClock>(sp => sp.GetRequiredService<QualifiedAuthorityClock>());
        builder.Services.AddSingleton(sp =>
        {
            var path = builder.Configuration["TeacherRevocation:SigningKeyPath"];
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new InvalidOperationException("TeacherRevocation:SigningKeyPath must be an absolute path.");
            var rsa = RSA.Create();
            try { rsa.ImportFromPem(File.ReadAllText(path)); }
            catch { rsa.Dispose(); throw; }
            var issuer = builder.Configuration["TeacherRevocation:Issuer"];
            var audience = builder.Configuration["TeacherRevocation:Audience"];
            return new ComponentJwt(rsa, issuer ?? string.Empty, audience ?? string.Empty,
                sp.GetRequiredService<IAuthorityClock>());
        });

        builder.Services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, CourseBearerHandler>(CourseBearerHandler.SchemeName, _ => { })
            .AddPolicyScheme(HybridScheme, HybridScheme, options =>
                options.ForwardDefaultSelector = context => LooksLikeComponentToken(context.Request.Headers.Authorization)
                    ? CourseBearerHandler.SchemeName : JwtBearerDefaults.AuthenticationScheme);
        builder.Services.Configure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = HybridScheme;
            options.DefaultChallengeScheme = HybridScheme;
        });
        // The enabled host defaults to the least-privilege S2/S3 capability path.
        // Owner-backed fixtures must opt out explicitly; there is no 42501 fallback.
        var restricted = builder.Configuration.GetValue<bool?>("TeacherRevocation:RestrictedDatabaseCapabilities") ?? true;
        return new(true, origin!, restricted, bootstrapEnabled);
    }

    private static bool LooksLikeComponentToken(string? authorization)
    {
        if (!System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(authorization, out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) || header.Parameter is not { } token)
            return false;
        var separator = token.IndexOf('.');
        if (separator <= 0) return false;
        try
        {
            var part = token[..separator].Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(part + new string('=', (4 - part.Length % 4) % 4));
            using var json = JsonDocument.Parse(bytes);
            var root = json.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("alg", out var alg) && alg.GetString() == "RS256" &&
                root.TryGetProperty("kid", out var kid) && kid.GetString() is { Length: 64 };
        }
        catch (Exception e) when (e is FormatException or JsonException) { return false; }
    }

    private sealed class RequestDbContextFactory(DbContextOptions<LmsDbContext> options) : IDbContextFactory<LmsDbContext>
    {
        public LmsDbContext CreateDbContext() => new(options);
        public Task<LmsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    // Synthetic Development only. This is not a deployment qualification or AUTH acceptance.
    private sealed class SyntheticDevelopmentClockSource : IQualifiedClockSource
    {
        private readonly long started = Stopwatch.GetTimestamp();
        private readonly DateTimeOffset utc = DateTimeOffset.UtcNow;
        private readonly string epoch = "synthetic-development-" + Guid.NewGuid().ToString("N");
        public QualifiedClockReading Read()
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            var now = utc + elapsed;
            return new(epoch, true, new(now, now), elapsed, elapsed);
        }
    }
}
