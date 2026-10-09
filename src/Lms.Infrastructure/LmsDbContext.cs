using Lms.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure;

public sealed class StoredDomainEvent
{
    public Guid Id { get; set; }
    public Guid ProblemWorkId { get; set; }
    public long Version { get; set; }
    public int Ordinal { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class LmsDbContext(DbContextOptions<LmsDbContext> options) : IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<ProblemAssignment> Assignments => Set<ProblemAssignment>();
    public DbSet<ProblemWork> Works => Set<ProblemWork>();
    public DbSet<StoredDomainEvent> Events => Set<StoredDomainEvent>();

    public DbSet<EnrollmentAccess> Enrollments => Set<EnrollmentAccess>();
    public DbSet<LessonAccess> Lessons => Set<LessonAccess>();
    public DbSet<TeacherAccess> TeacherGrants => Set<TeacherAccess>();
    public DbSet<AttachmentAccess> AttachmentMetadata => Set<AttachmentAccess>();

    public DbSet<StoredAttachment> StoredAttachments => Set<StoredAttachment>();

    public DbSet<PublishedContentVersion> ContentVersions => Set<PublishedContentVersion>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        SchemaFoundationMappings.Configure(model);
        model.Entity<EnrollmentAccess>().ToTable("AccessEnrollments").HasKey(x => new { x.StudentId, x.CourseId });
        model.Entity<LessonAccess>().ToTable("AccessLessons").HasKey(x => new { x.CourseId, x.CourseVersionId, x.LessonId });
        model.Entity<TeacherAccess>().ToTable("AccessTeachers").HasKey(x => x.Id);
        model.Entity<TeacherAccess>().HasIndex(x => new { x.TeacherId, x.CourseId });
        model.Entity<AttachmentAccess>().ToTable("AccessAttachments").HasKey(x => x.Id);
        var attachment = model.Entity<StoredAttachment>();
        attachment.ToTable("StoredAttachments", t => {
            t.HasCheckConstraint("CK_StoredAttachment_Size", "\"Size\" > 0");
            t.HasCheckConstraint("CK_StoredAttachment_Link", "NOT (\"SubmissionId\" IS NOT NULL AND \"MessageId\" IS NOT NULL) AND (NOT \"Deleted\" OR (\"SubmissionId\" IS NULL AND \"MessageId\" IS NULL))");
        });
        attachment.HasKey(x => x.Id);
        attachment.Property(x => x.Id).ValueGeneratedNever();
        attachment.HasIndex(x => x.StorageKey).IsUnique();
        attachment.HasOne<AttachmentAccess>().WithOne().HasForeignKey<StoredAttachment>(x => x.Id).OnDelete(DeleteBehavior.Restrict);
        attachment.HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        attachment.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Restrict);
        var content = model.Entity<PublishedContentVersion>();
        content.ToTable("PublishedContentVersions", t => t.HasCheckConstraint("CK_Content_Version", "\"VersionNumber\" > 0"));
        content.HasKey(x => x.Id);
        content.Property(x => x.Id).ValueGeneratedNever();
        content.HasIndex(x => new { x.CourseId, x.VersionNumber }).IsUnique();
        content.Property(x => x.ContentJson).HasColumnType("jsonb");
        var a = model.Entity<ProblemAssignment>();
        a.ToTable("ProblemAssignments", t => t.HasCheckConstraint("CK_Assignment_Status", "\"Status\" IN ('Active', 'Withdrawn')")); a.HasKey(x => x.Id);
        a.Property(x => x.Id).ValueGeneratedNever(); a.Property(x => x.StudentId);
        a.Property(x => x.CourseId); a.Property(x => x.Status).HasConversion<string>();
        a.HasAlternateKey(x => new { x.Id, x.StudentId });
        a.OwnsOne(x => x.Content, c =>
        {
            c.Property(x => x.CourseVersionId).HasColumnName("CourseVersionId");
            c.Property(x => x.LessonId).HasColumnName("LessonId");
            c.Property(x => x.ProblemId).HasColumnName("ProblemId");
        });
        a.Navigation(x => x.Content).IsRequired();
        var w = model.Entity<ProblemWork>();
        w.ToTable("ProblemWorks", t => { t.HasCheckConstraint("CK_Work_Version", "\"Version\" >= 0"); t.HasCheckConstraint("CK_Work_Status", "\"Status\" IN ('NotStarted', 'Submitted', 'NeedsRevision', 'Accepted')"); });
        w.HasKey(x => x.Id); w.Property(x => x.Id).ValueGeneratedNever();
        w.Property(x => x.ProblemAssignmentId); w.Property(x => x.StudentId);
        w.Property(x => x.Status).HasConversion<string>(); w.Property(x => x.Version).IsConcurrencyToken();
        w.Property(x => x.AcceptedAt);
        w.HasIndex(x => x.ProblemAssignmentId).IsUnique();
        w.HasOne<ProblemAssignment>().WithMany().HasForeignKey(x => new { x.ProblemAssignmentId, x.StudentId })
            .HasPrincipalKey(x => new { x.Id, x.StudentId }).OnDelete(DeleteBehavior.Restrict);
        w.Ignore(x => x.AttemptCount); w.Ignore(x => x.LastSubmissionAt); w.Ignore(x => x.LastReviewedAt); w.Ignore(x => x.Events);
        w.HasMany(x => x.Submissions).WithOne().HasForeignKey(x => x.ProblemWorkId).OnDelete(DeleteBehavior.Restrict);
        w.HasMany(x => x.Reviews).WithOne().HasForeignKey(x => x.ProblemWorkId).OnDelete(DeleteBehavior.Restrict);
        w.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ProblemWorkId).OnDelete(DeleteBehavior.Restrict);
        w.Navigation(x => x.Submissions).HasField("submissions").UsePropertyAccessMode(PropertyAccessMode.Field);
        w.Navigation(x => x.Reviews).HasField("reviews").UsePropertyAccessMode(PropertyAccessMode.Field);
        w.Navigation(x => x.Messages).HasField("messages").UsePropertyAccessMode(PropertyAccessMode.Field);
        var s = model.Entity<Submission>();
        s.ToTable("Submissions", t => t.HasCheckConstraint("CK_Submission_Attempt", "\"AttemptNumber\" > 0"));
        s.HasKey(x => x.Id); s.Property(x => x.Id).ValueGeneratedNever();
        s.Property(x => x.ProblemWorkId); s.Property(x => x.AttemptNumber); s.Property(x => x.BodyMarkdown);
        s.Property(x => x.SubmittedAt); s.Property(x => x.SubmittedBy); Attachments(s.Property(x => x.AttachmentIds));
        s.HasAlternateKey(x => new { x.ProblemWorkId, x.Id });
        s.HasIndex(x => new { x.ProblemWorkId, x.AttemptNumber }).IsUnique();
        var m = model.Entity<Message>();
        m.ToTable("Messages", t => t.HasCheckConstraint("CK_Message_Role", "\"AuthorRole\" IN ('Student', 'Teacher')")); m.HasKey(x => x.Id); m.Property(x => x.Id).ValueGeneratedNever();
        m.Property(x => x.ProblemWorkId); m.Property(x => x.AuthorId); m.Property(x => x.AuthorRole).HasConversion<string>();
        m.Property(x => x.BodyMarkdown); m.Property(x => x.ReplyToMessageId); m.Property(x => x.CreatedAt);
        Attachments(m.Property(x => x.AttachmentIds)); m.HasAlternateKey(x => new { x.ProblemWorkId, x.Id });
        m.HasOne<Message>().WithMany().HasForeignKey(x => new { x.ProblemWorkId, x.ReplyToMessageId })
            .HasPrincipalKey(x => new { x.ProblemWorkId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        var r = model.Entity<Review>();
        r.ToTable("Reviews", t => t.HasCheckConstraint("CK_Review_Decision", "\"Decision\" IN ('Accepted', 'NeedsRevision')")); r.HasKey(x => x.Id); r.Property(x => x.Id).ValueGeneratedNever();
        r.Property(x => x.ProblemWorkId); r.Property(x => x.SubmissionId); r.Property(x => x.TeacherId);
        r.Property(x => x.Decision).HasConversion<string>(); r.Property(x => x.MessageId); r.Property(x => x.CreatedAt);
        r.Property(x => x.Annotations).HasConversion(
            x => AnnotationJson.Serialize(x), x => AnnotationJson.Deserialize(x)).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        r.Property(x => x.Annotations).Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<AnnotatedAttachment>>(
            (a, b) => AnnotationJson.Serialize(a!) == AnnotationJson.Serialize(b!),
            x => AnnotationJson.Serialize(x).GetHashCode(), x => AnnotationJson.Deserialize(AnnotationJson.Serialize(x))));
        r.HasIndex(x => x.SubmissionId).IsUnique();
        r.HasOne<Submission>().WithMany().HasForeignKey(x => new { x.ProblemWorkId, x.SubmissionId })
            .HasPrincipalKey(x => new { x.ProblemWorkId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<Message>().WithMany().HasForeignKey(x => new { x.ProblemWorkId, x.MessageId })
            .HasPrincipalKey(x => new { x.ProblemWorkId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        var e = model.Entity<StoredDomainEvent>();
        e.ToTable("DomainEvents"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Payload).HasColumnType("jsonb");
        e.HasIndex(x => new { x.ProblemWorkId, x.Version, x.Ordinal }).IsUnique();
        e.HasOne<ProblemWork>().WithMany().HasForeignKey(x => x.ProblemWorkId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void Attachments(PropertyBuilder<IReadOnlyList<Guid>> property)
    {
        property.HasConversion(x => x.ToArray(), x => Array.AsReadOnly(x)).HasColumnType("uuid[]");
        property.Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<Guid>>(
            (a, b) => a!.SequenceEqual(b!), x => x.Aggregate(0, (h, id) => HashCode.Combine(h, id)),
            x => Array.AsReadOnly(x.ToArray())));
    }
}
