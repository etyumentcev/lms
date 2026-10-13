using System.Text.Json.Serialization;
using Lms.Api;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
if (args.Contains("--dev-token"))
{
    if (!LocalDevelopment.Enabled(builder)) throw new InvalidOperationException("Local tokens require Development and LocalDemo:Enabled.");
    var role = args.Contains("Teacher") ? "Teacher" : "Student";
    Console.WriteLine(LocalDevelopment.Token(builder.Configuration, role));
    return;
}
builder.Services.AddDbContext<LmsDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Lms") ?? builder.Configuration["LMS_POSTGRES"]
    ?? throw new InvalidOperationException("Configure ConnectionStrings:Lms or LMS_POSTGRES.")));
var attachmentOptions = new AttachmentOptions();
builder.Configuration.GetSection("Attachments").Bind(attachmentOptions);
if (attachmentOptions.MaxBytes <= 0 || attachmentOptions.MaxBytes > 100 * 1024 * 1024)
    throw new InvalidOperationException("Attachments:MaxBytes must be between 1 and 104857600.");
builder.Services.AddSingleton(attachmentOptions);
var localStorageOptions = new LocalAttachmentStorageOptions();
builder.Configuration.GetSection("Attachments").Bind(localStorageOptions);
builder.Services.AddSingleton(localStorageOptions);
builder.Services.AddSingleton<IAttachmentStorage, LocalFileAttachmentStorage>();
builder.Services.AddScoped<AttachmentService>();
builder.Services.AddScoped<IWorkStore, PostgresWorkStore>();
builder.Services.AddScoped<IAccessPolicy, PostgresAccessPolicy>();
builder.Services.AddScoped<ProblemWorkService>();
builder.Services.AddScoped<IImmutableContentResolver, PostgresContentResolver>();
builder.Services.AddScoped<IWorkspaceQueries, PostgresWorkspaceQueries>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IActorMapper, ClaimsActorMapper>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.Authority = builder.Configuration["Authentication:Authority"];
    options.Audience = builder.Configuration["Authentication:Audience"];
    options.RequireHttpsMetadata = true;
    options.MapInboundClaims = false;
    options.IncludeErrorDetails = false;
    options.TokenValidationParameters.ValidateIssuer = true;
    options.TokenValidationParameters.ValidateAudience = true;
    options.TokenValidationParameters.ValidateLifetime = true;
    options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var token = context.SecurityToken;
            if (token.ValidFrom == DateTime.MinValue || token.ValidTo <= token.ValidFrom ||
                token.ValidTo - token.ValidFrom > SessionPolicy.MaximumAccessLifetime)
                context.Fail("Invalid access token lifetime.");
            return Task.CompletedTask;
        }
    };
    options.TokenValidationParameters.RequireSignedTokens = true;
    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
    if (LocalDevelopment.Enabled(builder))
    {
        options.Authority = null;
        options.TokenValidationParameters.ValidIssuer = LocalDevelopment.Issuer;
        options.TokenValidationParameters.ValidAudience = LocalDevelopment.Audience;
        options.TokenValidationParameters.IssuerSigningKey = LocalDevelopment.Key(builder.Configuration);
    }
    // Without configured authority/audience no untrusted token can authenticate.
});
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
}).ConfigureApiBehaviorOptions(options =>
{
    options.SuppressMapClientErrors = true;
    options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(
        ApiErrors.Create(context.HttpContext, "ValidationFailed", 400))
    { ContentTypes = { "application/problem+json" } };
});
var teacherRevocation = builder.AddTeacherRevocationHost();
var app = builder.Build();
if (args.Contains("--migrate-only"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
    await db.Database.MigrateAsync();
    if (LocalDevelopment.Enabled(builder)) await LocalDevelopment.Seed(db);
    return;
}
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseStatusCodePages(async context =>
{
    var status = context.HttpContext.Response.StatusCode;
    var code = status switch { 401 => "Unauthenticated", 403 => "Forbidden", 404 => "NotFound", 405 => "MethodNotAllowed", _ => "ValidationFailed" };
    await ApiErrors.Write(context.HttpContext, code, status);
});
if (teacherRevocation.Enabled) app.UseMiddleware<CourseRequestIngress>();
app.UseAuthentication();
app.UseAuthorization();
if (teacherRevocation.OrganizationBootstrapEnabled) app.UseRateLimiter();
app.MapGet("/api/health", async (LmsDbContext db, CancellationToken ct) =>
{
    try { return await db.Database.CanConnectAsync(ct) ? Results.Ok() : Results.StatusCode(503); }
    catch { return Results.StatusCode(503); }
});
app.MapControllers();
if (teacherRevocation.Enabled)
{
    app.MapTeacherRevocation(teacherRevocation.AllowedOrigin,
        teacherRevocation.RestrictedDatabaseCapabilities);
    app.MapProgressHistory();
}
if (teacherRevocation.OrganizationBootstrapEnabled)
{
    app.MapOrganizationBootstrap(teacherRevocation.AllowedOrigin);
    app.MapAccountAdministration(teacherRevocation.AllowedOrigin);
    app.MapCourseRoster(teacherRevocation.AllowedOrigin);
    app.MapContentPublication(teacherRevocation.AllowedOrigin,
        app.Configuration.GetValue<long?>("ContentImport:MaxZipBytes") ?? 20_000_000);
}
app.Run();

public partial class Program { }
