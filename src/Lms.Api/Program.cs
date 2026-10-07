using System.Text.Json.Serialization;
using Lms.Api;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<LmsDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Lms") ?? builder.Configuration["LMS_POSTGRES"]
    ?? throw new InvalidOperationException("Configure ConnectionStrings:Lms or LMS_POSTGRES.")));
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
    options.TokenValidationParameters.RequireSignedTokens = true;
    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
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
var app = builder.Build();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseStatusCodePages(async context =>
{
    var status = context.HttpContext.Response.StatusCode;
    var code = status switch { 401 => "Unauthenticated", 403 => "Forbidden", 404 => "NotFound", 405 => "MethodNotAllowed", _ => "ValidationFailed" };
    await ApiErrors.Write(context.HttpContext, code, status);
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }
