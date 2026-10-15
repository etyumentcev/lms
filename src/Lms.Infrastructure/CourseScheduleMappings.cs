using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

public sealed partial class LmsDbContext
{
    private static void ConfigureCourseSchedule(ModelBuilder model)
    {
        model.Entity<CourseCatalogRecord>().HasAlternateKey(x => new { x.CourseId, x.OrganizationId });

        var workload = model.Entity<CourseWorkload>();
        workload.ToTable("CourseWorkloads", table =>
        {
            table.HasCheckConstraint("CK_CourseWorkload_Hours", "\"TotalAcademicHours\" >= 0 AND \"ScheduledAcademicHours\" >= 0 AND \"IndependentAcademicHours\" >= 0 AND \"TotalAcademicHours\" = \"ScheduledAcademicHours\" + \"IndependentAcademicHours\"");
            table.HasCheckConstraint("CK_CourseWorkload_Minutes", "\"AcademicHourMinutes\" BETWEEN 1 AND 180");
            table.HasCheckConstraint("CK_CourseWorkload_Version", "\"Version\" > 0");
        });
        workload.HasKey(x => x.CourseId);
        workload.Property(x => x.Version).IsConcurrencyToken();
        workload.HasOne<CourseCatalogRecord>().WithOne().
            HasForeignKey<CourseWorkload>(x => new { x.CourseId, x.OrganizationId }).
            HasPrincipalKey<CourseCatalogRecord>(x => new { x.CourseId, x.OrganizationId }).
            OnDelete(DeleteBehavior.Restrict);
        workload.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        workload.HasOne<ActorRecord>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.Restrict);

        var rule = model.Entity<CourseScheduleRule>();
        rule.ToTable("CourseScheduleRules", table =>
        {
            table.HasCheckConstraint("CK_CourseScheduleRule_Day", "\"IsoDayOfWeek\" BETWEEN 1 AND 7");
            table.HasCheckConstraint("CK_CourseScheduleRule_Hours", "\"AcademicHours\" BETWEEN 1 AND 24");
            table.HasCheckConstraint("CK_CourseScheduleRule_Period", "\"EffectiveTo\" >= \"EffectiveFrom\" AND \"EffectiveTo\" <= \"EffectiveFrom\" + 731");
            table.HasCheckConstraint("CK_CourseScheduleRule_Mode", "\"DeliveryMode\" IN ('InPerson','Online','Hybrid')");
            table.HasCheckConstraint("CK_CourseScheduleRule_Kind", "\"EventKind\" IN ('Lesson','Consultation','ExtraLesson')");
            table.HasCheckConstraint("CK_CourseScheduleRule_Version", "\"Version\" > 0");
        });
        rule.HasKey(x => x.RuleId); rule.Property(x => x.RuleId).ValueGeneratedNever();
        rule.Property(x => x.Version).IsConcurrencyToken();
        rule.HasIndex(x => new { x.CourseId, x.IsActive, x.EffectiveFrom, x.EffectiveTo });
        rule.HasOne<CourseCatalogRecord>().WithMany().
            HasForeignKey(x => new { x.CourseId, x.OrganizationId }).
            HasPrincipalKey(x => new { x.CourseId, x.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        rule.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        rule.HasOne<ActorRecord>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.Restrict);

        var exception = model.Entity<CourseScheduleException>();
        exception.ToTable("CourseScheduleExceptions", table =>
        {
            table.HasCheckConstraint("CK_CourseScheduleException_Status", "\"Status\" IN ('Moved','Cancelled')");
            table.HasCheckConstraint("CK_CourseScheduleException_Move", "(\"Status\" = 'Cancelled' AND \"NewDate\" IS NULL AND \"NewStartTime\" IS NULL AND \"NewEndTime\" IS NULL) OR (\"Status\" = 'Moved' AND \"NewDate\" IS NOT NULL AND \"NewStartTime\" IS NOT NULL AND \"NewEndTime\" IS NOT NULL)");
            table.HasCheckConstraint("CK_CourseScheduleException_Version", "\"Version\" > 0");
        });
        exception.HasKey(x => x.ExceptionId); exception.Property(x => x.ExceptionId).ValueGeneratedNever();
        exception.Property(x => x.Version).IsConcurrencyToken();
        exception.HasIndex(x => new { x.RuleId, x.OriginalDate }).IsUnique();
        exception.HasOne<CourseScheduleRule>().WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Restrict);
        exception.HasOne<CourseCatalogRecord>().WithMany().
            HasForeignKey(x => new { x.CourseId, x.OrganizationId }).
            HasPrincipalKey(x => new { x.CourseId, x.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        exception.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        exception.HasOne<ActorRecord>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.Restrict);

        var receipt = model.Entity<CourseScheduleCommandReceipt>();
        receipt.ToTable("CourseScheduleCommandReceipts");
        receipt.HasKey(x => new { x.AdministratorActorId, x.IdempotencyKey });
        receipt.Property(x => x.ResultJson).HasColumnType("jsonb");
        receipt.HasOne<OrganizationMembership>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.AdministratorActorId })
            .HasPrincipalKey(x => new { x.OrganizationId, x.ActorId }).OnDelete(DeleteBehavior.Restrict);
        receipt.HasOne<CourseCatalogRecord>().WithMany().
            HasForeignKey(x => new { x.CourseId, x.OrganizationId }).
            HasPrincipalKey(x => new { x.CourseId, x.OrganizationId }).OnDelete(DeleteBehavior.Restrict);

        var audit = model.Entity<CourseScheduleAudit>();
        audit.ToTable("CourseScheduleAudit");
        audit.HasKey(x => x.AuditId); audit.Property(x => x.AuditId).ValueGeneratedNever();
        audit.HasIndex(x => new { x.OrganizationId, x.CourseId, x.OccurredAt });
        audit.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        audit.HasOne<CourseCatalogRecord>().WithMany().
            HasForeignKey(x => new { x.CourseId, x.OrganizationId }).
            HasPrincipalKey(x => new { x.CourseId, x.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        audit.HasOne<ActorRecord>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
