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
    public DbSet<ContentImportAttempt> ContentImports => Set<ContentImportAttempt>();
    public DbSet<ContentImportAsset> ContentImportAssets => Set<ContentImportAsset>();
    public DbSet<PublishedContentArtifact> ContentArtifacts => Set<PublishedContentArtifact>();
    public DbSet<PublishedContentAsset> ContentAssets => Set<PublishedContentAsset>();
    public DbSet<LessonGroupOpening> LessonGroupOpenings => Set<LessonGroupOpening>();
    public DbSet<ContentOperationReceipt> ContentOperationReceipts => Set<ContentOperationReceipt>();

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
        var import = model.Entity<ContentImportAttempt>();
        import.ToTable("ContentImportAttempts", t =>
        {
            t.HasCheckConstraint("CK_ContentImport_State", "\"State\" IN ('Validating','Ready','Invalid','Failed')");
            t.HasCheckConstraint("CK_ContentImport_Version", "\"Version\" > 0 AND \"ExpiresAt\" > \"CreatedAt\"");
            t.HasCheckConstraint("CK_ContentImport_Digests", "char_length(\"RequestDigest\") = 64 AND (\"State\" <> 'Ready' OR (char_length(\"StudentProjectionHash\") = 64 AND char_length(\"PackageHash\") = 64 AND char_length(\"TeacherArtifactHash\") = 64))");
            t.HasCheckConstraint("CK_ContentImport_Result", "(\"State\" = 'Ready' AND \"StudentArtifactJson\" IS NOT NULL AND \"TeacherArtifactJson\" IS NOT NULL AND \"ReceiptJson\" IS NOT NULL) OR (\"State\" <> 'Ready' AND \"StudentArtifactJson\" IS NULL AND \"TeacherArtifactJson\" IS NULL AND \"ReceiptJson\" IS NULL AND \"PublishedVersionId\" IS NULL)");
        });
        import.HasKey(x => x.ImportId); import.Property(x => x.ImportId).ValueGeneratedNever();
        import.HasIndex(x => new { x.OwnerActorId, x.IdempotencyKey }).IsUnique();
        import.Property(x => x.DiagnosticsJson).HasColumnType("jsonb");
        import.Property(x => x.StudentArtifactJson).HasColumnType("jsonb");
        import.Property(x => x.TeacherArtifactJson).HasColumnType("jsonb");
        import.Property(x => x.ReceiptJson).HasColumnType("jsonb");
        import.HasOne<CourseRegistration>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        import.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        import.HasOne<ActorRecord>().WithMany().HasForeignKey(x => x.OwnerActorId).OnDelete(DeleteBehavior.Restrict);
        var importAsset = model.Entity<ContentImportAsset>();
        importAsset.ToTable("ContentImportAssets", t =>
        {
            t.HasCheckConstraint("CK_ContentImportAsset_Audience", "\"Audience\" IN ('student','teacher')");
            t.HasCheckConstraint("CK_ContentImportAsset_Hash", "char_length(\"Sha256\") = 64 AND octet_length(\"Bytes\") > 0");
        });
        importAsset.HasKey(x => new { x.ImportId, x.Audience, x.AssetPath });
        importAsset.HasOne<ContentImportAttempt>().WithMany().HasForeignKey(x => x.ImportId).OnDelete(DeleteBehavior.Restrict);
        var artifact = model.Entity<PublishedContentArtifact>();
        artifact.ToTable("PublishedContentArtifacts", t => t.HasCheckConstraint("CK_PublishedContentArtifact_Hashes", "char_length(\"StudentProjectionHash\") = 64 AND char_length(\"PackageHash\") = 64 AND char_length(\"TeacherArtifactHash\") = 64")); artifact.HasKey(x => x.ContentVersionId);
        artifact.Property(x => x.ContentVersionId).ValueGeneratedNever();
        artifact.Property(x => x.StudentArtifactJson).HasColumnType("jsonb");
        artifact.Property(x => x.TeacherArtifactJson).HasColumnType("jsonb");
        artifact.HasOne<PublishedContentVersion>().WithOne().HasForeignKey<PublishedContentArtifact>(x => x.ContentVersionId).OnDelete(DeleteBehavior.Restrict);
        artifact.HasOne<ContentImportAttempt>().WithOne().HasForeignKey<PublishedContentArtifact>(x => x.ImportId).OnDelete(DeleteBehavior.Restrict);
        var asset = model.Entity<PublishedContentAsset>();
        asset.ToTable("PublishedContentAssets", t =>
        {
            t.HasCheckConstraint("CK_PublishedContentAsset_Audience", "\"Audience\" IN ('student','teacher')");
            t.HasCheckConstraint("CK_PublishedContentAsset_Hash", "char_length(\"Sha256\") = 64 AND octet_length(\"Bytes\") > 0");
        });
        asset.HasKey(x => new { x.ContentVersionId, x.Audience, x.AssetPath });
        asset.HasOne<PublishedContentVersion>().WithMany().HasForeignKey(x => x.ContentVersionId).OnDelete(DeleteBehavior.Restrict);
        var opening = model.Entity<LessonGroupOpening>();
        opening.ToTable("LessonGroupOpenings");
        opening.HasKey(x => new { x.CourseId, x.GroupId, x.ContentVersionId, x.LessonId });
        opening.HasOne<CourseGroup>().WithMany().HasForeignKey(x => new { x.CourseId, x.GroupId }).OnDelete(DeleteBehavior.Restrict);
        opening.HasOne<PublishedContentVersion>().WithMany().HasForeignKey(x => x.ContentVersionId).OnDelete(DeleteBehavior.Restrict);
        opening.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        opening.HasOne<ActorRecord>().WithMany().HasForeignKey(x => x.OpenedBy).OnDelete(DeleteBehavior.Restrict);
        var receipt = model.Entity<ContentOperationReceipt>();
        receipt.ToTable("ContentOperationReceipts", t => t.HasCheckConstraint("CK_ContentOperationReceipt_Digest", "char_length(\"PayloadDigest\") = 64"));
        receipt.HasKey(x => new { x.ActorId, x.IdempotencyKey });
        receipt.Property(x => x.ResultJson).HasColumnType("jsonb");
        receipt.HasOne<ActorRecord>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        receipt.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        receipt.HasOne<CourseRegistration>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
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
