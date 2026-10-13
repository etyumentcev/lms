using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Lms.Api.Tests;

public sealed partial class IsolatedProcessingDeploymentTests
{
    [BrowserReadTheory]
    [InlineData(true)]
    public async Task React_browser_login_reload_expiry_two_tabs_switch_replay_and_logout(bool browser)
    {
        Assert.True(browser);
        var identity=await IdentitySeed(); var secondIdentity=await IdentitySeed();
        var orgB=Guid.NewGuid(); var contextA=Guid.NewGuid(); var contextB=Guid.NewGuid();
        const string courseA="auth-course-a", courseB="auth-course-b";
        await Sql(owner,$"INSERT INTO \"Organizations\" VALUES('{orgB}','Enabled',true); INSERT INTO \"CourseRegistry\" VALUES('{courseA}','{existingOrg}','Verified'),('{courseB}','{orgB}','Verified'); INSERT INTO \"CourseGroups\" VALUES('{courseA}','g'),('{courseB}','g')");
        await ChangeRestricted(new(AuthorityKind.Membership,AuthorityOperation.Grant,existingOrg,identity.Actor,0,Role:ScopeRole.Student));
        await ChangeRestricted(new(AuthorityKind.Enrollment,AuthorityOperation.Grant,existingOrg,identity.Actor,0,1,courseA,"g"));
        await ChangeRestricted(new(AuthorityKind.Membership,AuthorityOperation.Grant,orgB,identity.Actor,0,Role:ScopeRole.Student));
        await ChangeRestricted(new(AuthorityKind.Enrollment,AuthorityOperation.Grant,orgB,identity.Actor,0,1,courseB,"g"));
        await ChangeRestricted(new(AuthorityKind.Membership,AuthorityOperation.Grant,orgB,secondIdentity.Actor,0,Role:ScopeRole.Student));
        await ChangeRestricted(new(AuthorityKind.Enrollment,AuthorityOperation.Grant,orgB,secondIdentity.Actor,0,1,courseB,"g"));
        var time=new HttpClock(); var clock=time.Create();
        await using(var db=Open(processing))
        {
            await new PostgresProcessingStateAdministration(db).Change(new(existingOrg,Guid.NewGuid(),0,new AddProcessingContext(contextA,1,time.At.AddHours(-1))));
            await new PostgresProcessingStateAdministration(db).Change(new(existingOrg,Guid.NewGuid(),1,new OpenProcessingBinding(courseA,contextA,1,time.At.AddMinutes(-30))));
            await new PostgresProcessingStateAdministration(db).Change(new(orgB,Guid.NewGuid(),0,new AddProcessingContext(contextB,1,time.At.AddHours(-1))));
            await new PostgresProcessingStateAdministration(db).Change(new(orgB,Guid.NewGuid(),1,new OpenProcessingBinding(courseB,contextB,1,time.At.AddMinutes(-30))));
        }
        await using(var db=Open(owner))
        {
            db.AddRange(
                new PublishedContentVersion{Id=Guid.NewGuid(),CourseId=courseA,VersionNumber=1,SourceRevision="auth-a",PublishedAt=time.At,
                    ContentJson=JsonSerializer.Serialize(new PublishedCourseContent("Организация А","Синтетический курс",[]),new JsonSerializerOptions(JsonSerializerDefaults.Web))},
                new PublishedContentVersion{Id=Guid.NewGuid(),CourseId=courseB,VersionNumber=1,SourceRevision="auth-b",PublishedAt=time.At,
                    ContentJson=JsonSerializer.Serialize(new PublishedCourseContent("Организация Б","Синтетический курс",[]),new JsonSerializerOptions(JsonSerializerDefaults.Web))});
            await db.SaveChangesAsync();
        }

        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null&&!File.Exists(Path.Combine(root.FullName,"Lms.sln"))) root=root.Parent;
        Assert.NotNull(root);
        var web=Path.Combine(root.FullName,"src/Lms.Web/dist");
        Assert.True(File.Exists(Path.Combine(web,"index.html")),"Build the production React UI first");
        using var signing=RSA.Create(2048); var jwt=new ComponentJwt(signing,"browser-auth","browser-auth",clock);
        using var tls=RSA.Create(2048);
        var request=new CertificateRequest("CN=localhost",tls,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var san=new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(san.Build());
        using var certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
        var listener=new TcpListener(IPAddress.Loopback,0); listener.Start(); var port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin=$"https://127.0.0.1:{port}";

        var builder=WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options=>options.Listen(IPAddress.Loopback,port,listen=>listen.UseHttps(certificate)));
        builder.Services.AddSingleton(clock); builder.Services.AddSingleton<IAuthorityClock>(clock); builder.Services.AddSingleton(jwt);
        builder.Services.AddSingleton<TimeProvider>(new HttpTimeProvider(time));
        builder.Services.AddDbContextFactory<LmsDbContext>(options=>options.UseNpgsql(runtime));
        builder.Services.AddScoped(_=>new HttpDatabases(runtime));
        builder.Services.AddScoped(sp=>new ComponentAuthorization(
            new PostgresScopedAuthorityReader(sp.GetRequiredService<HttpDatabases>().Resources,new HttpTimeProvider(time)),
            new PostgresCurrentProcessingGate(sp.GetRequiredService<HttpDatabases>().Processing,clock),clock));
        builder.Services.AddScoped<ICourseNavigationQueries>(sp=>new PostgresCourseNavigationQueries(
            sp.GetRequiredService<HttpDatabases>().Content,sp.GetRequiredService<ComponentAuthorization>(),clock));
        builder.Services.AddSingleton(new AttachmentOptions{MaxBytes=1024*1024});
        builder.Services.AddSingleton<IAttachmentStorage>(new LocalFileAttachmentStorage(new(){Root=Path.Combine(Path.GetTempPath(),"auth-browser-unused")}));
        builder.Services.AddScoped<PostgresTeacherCommands>();
        builder.Services.AddScoped<AttachmentService>(); builder.Services.AddScoped<IWorkStore,PostgresWorkStore>();
        builder.Services.AddScoped<IAccessPolicy,PostgresAccessPolicy>(); builder.Services.AddScoped<ProblemWorkService>();
        builder.Services.AddSingleton<IActorMapper,ClaimsActorMapper>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddTeacherRevocationBrowserProtection();
        builder.Services.AddAuthentication(CourseBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions,CourseBearerHandler>(CourseBearerHandler.SchemeName,_=>{});
        builder.Services.AddAuthorization();
        await using var app=builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>(); app.UseMiddleware<CourseRequestIngress>();
        app.UseAuthentication(); app.UseAuthorization();
        app.MapTeacherRevocation(origin,true); app.MapProtectedCourses();
        app.UseStaticFiles(new StaticFileOptions{FileProvider=new PhysicalFileProvider(web)});
        app.MapFallback(async http=>{
            if(http.Request.Path.StartsWithSegments("/api")){http.Response.StatusCode=404;return;}
            http.Response.ContentType="text/html; charset=utf-8";
            await Microsoft.AspNetCore.Http.SendFileResponseExtensions.SendFileAsync(http.Response,Path.Combine(web,"index.html"));
        });
        await app.StartAsync();

        var evidence=Path.Combine(Environment.GetEnvironmentVariable("LMS_BROWSER_EVIDENCE_ROOT")??Path.Combine(root.FullName,"codex/evidence"),"M1-01-BROWSER-AUTH");
        Directory.CreateDirectory(evidence);
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("LMS_BROWSER_NODE")??"node"){
            WorkingDirectory=root.FullName,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
        start.ArgumentList.Add(Path.Combine(root.FullName,"tools/browser_read/react-browser-session.mjs"));
        using var runner=Process.Start(start)!; using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var stderr=runner.StandardError.ReadToEndAsync(timeout.Token);
        await runner.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{origin,userName=identity.Name,password=identity.Password,
            secondUserName=secondIdentity.Name,secondPassword=secondIdentity.Password,secondOrganization=orgB,organizationA=existingOrg,organizationB=orgB,foreignOrganization=Guid.NewGuid(),courseA="Организация А",courseB="Организация Б",evidence}));
        var passed=false;
        try
        {
            while(await runner.StandardOutput.ReadLineAsync(timeout.Token) is {} command)
            {
                if(command=="PASS"){passed=true;break;}
                switch(command)
                {
                    case "expire": time.Elapsed=TimeSpan.FromMinutes(16); break;
                    case "revoke-a": await ChangeRestricted(new(AuthorityKind.Membership,AuthorityOperation.Revoke,existingOrg,identity.Actor,1)); break;
                    default: throw new InvalidOperationException("Unknown browser session command");
                }
                await runner.StandardInput.WriteLineAsync("{}");
            }
            await runner.WaitForExitAsync(timeout.Token);
            var diagnostic=(await stderr).Replace(identity.Password,"[REDACTED]").Replace(identity.Name,"[REDACTED]").Replace(secondIdentity.Password,"[REDACTED]").Replace(secondIdentity.Name,"[REDACTED]");
            Assert.True(passed&&runner.ExitCode==0,diagnostic);
        }
        finally{if(!runner.HasExited)runner.Kill(true);}

        await using var verify=Open(owner);
        var sessions=await verify.Set<IdentitySession>().ToListAsync();
        Assert.Equal(2,sessions.Count);
        Assert.All(sessions,session=>Assert.NotNull(session.RevokedAt));
        Assert.Equal(2,await verify.Set<SecurityAudit>().CountAsync(x=>x.Action=="session_created"));
        Assert.Equal(2,await verify.Set<SecurityAudit>().CountAsync(x=>x.Action=="session_revoked"));
    }
}
