using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SchemaFoundationsM1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Actors",
                columns: table => new
                {
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryEducationalRole = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Actors", x => x.ActorId);
                    table.CheckConstraint("CK_Actor_Role", "\"PrimaryEducationalRole\" IS NULL OR \"PrimaryEducationalRole\" IN ('Student','Teacher')");
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    SyntheticOnly = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.OrganizationId);
                    table.CheckConstraint("CK_Organization_State", "\"State\" IN ('Enabled','Suspended')");
                    table.CheckConstraint("CK_Organization_Synthetic", "\"SyntheticOnly\"");
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountSecurityState",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialEpoch = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReasonRef = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountSecurityState", x => x.UserId);
                    table.CheckConstraint("CK_Security_State", "\"State\" IN ('Enabled','SecurityBlocked') AND \"CredentialEpoch\" > 0 AND \"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_AccountSecurityState_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdentitySessions",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSuccessfulRefreshAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AbsoluteExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevocationReasonRef = table.Column<string>(type: "text", nullable: true),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentitySessions", x => x.SessionId);
                    table.CheckConstraint("CK_Session_Times", "\"Generation\" > 0 AND \"Version\" > 0 AND \"AbsoluteExpiresAt\" = \"CreatedAt\" + interval '12 hours' AND \"LastSuccessfulRefreshAt\" >= \"CreatedAt\" AND \"LastSuccessfulRefreshAt\" <= \"AbsoluteExpiresAt\" AND (\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\")");
                    table.ForeignKey(
                        name: "FK_IdentitySessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActorIdentities",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ProvisioningOrgId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActorIdentities", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_ActorIdentities_Actors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Actors",
                        principalColumn: "ActorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActorIdentities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActorIdentities_Organizations_ProvisioningOrgId",
                        column: x => x.ProvisioningOrgId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourseRegistry",
                columns: table => new
                {
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    MappingState = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseRegistry", x => x.CourseId);
                    table.CheckConstraint("CK_Course_Mapping", "(\"MappingState\" = 'Verified' AND \"OrganizationId\" IS NOT NULL) OR (\"MappingState\" = 'Unknown' AND \"OrganizationId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_CourseRegistry_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMemberships",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    EducationalRole = table.Column<string>(type: "text", nullable: true),
                    PermissionFlags = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RevocationRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccessValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMemberships", x => new { x.OrganizationId, x.ActorId });
                    table.CheckConstraint("CK_Membership_Permissions", "\"PermissionFlags\" BETWEEN 0 AND 15");
                    table.CheckConstraint("CK_Membership_Role", "\"EducationalRole\" IS NULL OR \"EducationalRole\" IN ('Student','Teacher')");
                    table.CheckConstraint("CK_OrganizationMembership_Deadline", "(\"RevocationRequestedAt\" IS NULL AND \"AccessValidUntil\" IS NULL AND \"State\" = 'Active') OR (\"RevocationRequestedAt\" IS NOT NULL AND \"AccessValidUntil\" IS NOT NULL AND \"State\" = 'Revoked' AND \"AccessValidUntil\" <= \"RevocationRequestedAt\" + interval '2 hours')");
                    table.CheckConstraint("CK_OrganizationMembership_Generation", "\"Generation\" > 0 AND \"Version\" > 0");
                    table.CheckConstraint("CK_OrganizationMembership_State", "\"State\" IN ('Active','Revoked')");
                    table.ForeignKey(
                        name: "FK_OrganizationMemberships_Actors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Actors",
                        principalColumn: "ActorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationMemberships_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingContextVersions",
                columns: table => new
                {
                    ContextId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurposeRef = table.Column<string>(type: "text", nullable: true),
                    LegalBasisRef = table.Column<string>(type: "text", nullable: true),
                    RetentionPolicyRef = table.Column<string>(type: "text", nullable: true),
                    ApprovalState = table.Column<string>(type: "text", nullable: false),
                    SyntheticOnly = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingContextVersions", x => new { x.ContextId, x.Version });
                    table.UniqueConstraint("AK_ProcessingContextVersions_ContextId_Version_OrganizationId", x => new { x.ContextId, x.Version, x.OrganizationId });
                    table.CheckConstraint("CK_Context_Synthetic", "\"SyntheticOnly\" AND \"ApprovalState\" IN ('Unreviewed','SyntheticOnly') AND \"LegalBasisRef\" IS NULL");
                    table.CheckConstraint("CK_Context_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_ProcessingContextVersions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProvisioningRequests",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalRosterKey = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadDigest = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisioningRequests", x => new { x.OrganizationId, x.ExternalRosterKey });
                    table.CheckConstraint("CK_Provision_State", "(\"State\" = 'Pending' AND \"UserId\" IS NULL) OR (\"State\" = 'Completed' AND \"UserId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ProvisioningRequests_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProvisioningRequests_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefreshVerifiers",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    Verifier = table.Column<byte[]>(type: "bytea", nullable: false),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshVerifiers", x => new { x.SessionId, x.Generation });
                    table.CheckConstraint("CK_Refresh_Verifier", "\"Generation\" > 0 AND octet_length(\"Verifier\") = 32 AND \"KeyVersion\" > 0 AND (\"ConsumedAt\" IS NULL OR \"ConsumedAt\" >= \"CreatedAt\")");
                    table.ForeignKey(
                        name: "FK_RefreshVerifiers_IdentitySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "IdentitySessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourseGroups",
                columns: table => new
                {
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    GroupId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseGroups", x => new { x.CourseId, x.GroupId });
                    table.ForeignKey(
                        name: "FK_CourseGroups_CourseRegistry_CourseId",
                        column: x => x.CourseId,
                        principalTable: "CourseRegistry",
                        principalColumn: "CourseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryRequests",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiatingOrgId = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiatorActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApproverActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    VerificationRef = table.Column<string>(type: "text", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryRequests", x => x.RequestId);
                    table.UniqueConstraint("AK_RecoveryRequests_RequestId_TargetUserId", x => new { x.RequestId, x.TargetUserId });
                    table.CheckConstraint("CK_Recovery_Approval", "(\"ApproverActorId\" IS NULL AND \"ApprovedAt\" IS NULL AND \"State\" NOT IN ('Approved','Fulfilled')) OR (\"ApproverActorId\" IS NOT NULL AND \"ApproverActorId\" <> \"InitiatorActorId\" AND \"ApprovedAt\" IS NOT NULL AND \"VerificationRef\" IS NOT NULL)");
                    table.CheckConstraint("CK_Recovery_State", "\"State\" IN ('Requested','Approved','Rejected','Suspended','Fulfilled','Cancelled') AND \"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_RecoveryRequests_Actors_ApproverActorId",
                        column: x => x.ApproverActorId,
                        principalTable: "Actors",
                        principalColumn: "ActorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecoveryRequests_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecoveryRequests_OrganizationMemberships_InitiatingOrgId_In~",
                        columns: x => new { x.InitiatingOrgId, x.InitiatorActorId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "ActorId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttachmentScopes",
                columns: table => new
                {
                    AttachmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    UploaderActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextVersion = table.Column<long>(type: "bigint", nullable: false),
                    Quarantined = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttachmentScopes", x => x.AttachmentId);
                    table.ForeignKey(
                        name: "FK_AttachmentScopes_OrganizationMemberships_OrganizationId_Upl~",
                        columns: x => new { x.OrganizationId, x.UploaderActorId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "ActorId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttachmentScopes_ProcessingContextVersions_ContextId_Contex~",
                        columns: x => new { x.ContextId, x.ContextVersion, x.OrganizationId },
                        principalTable: "ProcessingContextVersions",
                        principalColumns: new[] { "ContextId", "Version", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttachmentScopes_StoredAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "StoredAttachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourseContextBindings",
                columns: table => new
                {
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextVersion = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseContextBindings", x => new { x.CourseId, x.EffectiveFrom });
                    table.CheckConstraint("CK_Binding_Interval", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" > \"EffectiveFrom\"");
                    table.ForeignKey(
                        name: "FK_CourseContextBindings_ProcessingContextVersions_ContextId_C~",
                        columns: x => new { x.ContextId, x.ContextVersion, x.OrganizationId },
                        principalTable: "ProcessingContextVersions",
                        principalColumns: new[] { "ContextId", "Version", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingRestrictions",
                columns: table => new
                {
                    RestrictionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginVersion = table.Column<long>(type: "bigint", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResourceRef = table.Column<string>(type: "text", nullable: true),
                    Operations = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingRestrictions", x => x.RestrictionId);
                    table.CheckConstraint("CK_Restriction_State", "\"State\" IN ('Active','Withdrawn') AND \"Version\" > 0 AND \"Operations\" > 0 AND \"Operations\" <= 31");
                    table.ForeignKey(
                        name: "FK_ProcessingRestrictions_OrganizationMemberships_Organization~",
                        columns: x => new { x.OrganizationId, x.SubjectActorId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "ActorId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessingRestrictions_ProcessingContextVersions_ContextId_~",
                        columns: x => new { x.ContextId, x.OriginVersion, x.OrganizationId },
                        principalTable: "ProcessingContextVersions",
                        principalColumns: new[] { "ContextId", "Version", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SecurityAudit",
                columns: table => new
                {
                    AuditId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: false),
                    SubjectRef = table.Column<string>(type: "text", nullable: false),
                    ResourceRef = table.Column<string>(type: "text", nullable: true),
                    Result = table.Column<string>(type: "text", nullable: false),
                    AuthorityVersion = table.Column<long>(type: "bigint", nullable: true),
                    ContextId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContextVersion = table.Column<long>(type: "bigint", nullable: true),
                    TraceId = table.Column<string>(type: "text", nullable: false),
                    EvidenceRef = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAudit", x => x.AuditId);
                    table.CheckConstraint("CK_Audit_Context", "(\"ContextId\" IS NULL AND \"ContextVersion\" IS NULL) OR (\"ContextId\" IS NOT NULL AND \"ContextVersion\" IS NOT NULL AND \"OrganizationId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_SecurityAudit_Actors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Actors",
                        principalColumn: "ActorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SecurityAudit_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SecurityAudit_ProcessingContextVersions_ContextId_ContextVe~",
                        columns: x => new { x.ContextId, x.ContextVersion, x.OrganizationId },
                        principalTable: "ProcessingContextVersions",
                        principalColumns: new[] { "ContextId", "Version", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EnrollmentScopes",
                columns: table => new
                {
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<string>(type: "text", nullable: true),
                    MembershipGeneration = table.Column<long>(type: "bigint", nullable: false),
                    EnrollmentGeneration = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RevocationRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccessValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentScopes", x => new { x.StudentId, x.CourseId });
                    table.CheckConstraint("CK_EnrollmentScope_Deadline", "(\"RevocationRequestedAt\" IS NULL AND \"AccessValidUntil\" IS NULL AND \"State\" = 'Active') OR (\"RevocationRequestedAt\" IS NOT NULL AND \"AccessValidUntil\" IS NOT NULL AND \"State\" = 'Revoked' AND \"AccessValidUntil\" <= \"RevocationRequestedAt\" + interval '2 hours')");
                    table.CheckConstraint("CK_EnrollmentScope_Generation", "\"EnrollmentGeneration\" > 0 AND \"Version\" > 0 AND \"MembershipGeneration\" > 0");
                    table.CheckConstraint("CK_EnrollmentScope_State", "\"State\" IN ('Active','Revoked')");
                    table.ForeignKey(
                        name: "FK_EnrollmentScopes_AccessEnrollments_StudentId_CourseId",
                        columns: x => new { x.StudentId, x.CourseId },
                        principalTable: "AccessEnrollments",
                        principalColumns: new[] { "StudentId", "CourseId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnrollmentScopes_CourseGroups_CourseId_GroupId",
                        columns: x => new { x.CourseId, x.GroupId },
                        principalTable: "CourseGroups",
                        principalColumns: new[] { "CourseId", "GroupId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnrollmentScopes_OrganizationMemberships_OrganizationId_Stu~",
                        columns: x => new { x.OrganizationId, x.StudentId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "ActorId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeacherAssignmentScopes",
                columns: table => new
                {
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    GroupId = table.Column<string>(type: "text", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipGeneration = table.Column<long>(type: "bigint", nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RevocationRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccessValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherAssignmentScopes", x => x.GrantId);
                    table.CheckConstraint("CK_TeacherAssignmentScope_Deadline", "(\"RevocationRequestedAt\" IS NULL AND \"AccessValidUntil\" IS NULL AND \"State\" = 'Active') OR (\"RevocationRequestedAt\" IS NOT NULL AND \"AccessValidUntil\" IS NOT NULL AND \"State\" = 'Revoked' AND \"AccessValidUntil\" <= \"RevocationRequestedAt\" + interval '2 hours')");
                    table.CheckConstraint("CK_TeacherAssignmentScope_Generation", "\"Generation\" > 0 AND \"Version\" > 0 AND \"MembershipGeneration\" > 0");
                    table.CheckConstraint("CK_TeacherAssignmentScope_State", "\"State\" IN ('Active','Revoked')");
                    table.ForeignKey(
                        name: "FK_TeacherAssignmentScopes_AccessTeachers_GrantId",
                        column: x => x.GrantId,
                        principalTable: "AccessTeachers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeacherAssignmentScopes_CourseGroups_CourseId_GroupId",
                        columns: x => new { x.CourseId, x.GroupId },
                        principalTable: "CourseGroups",
                        principalColumns: new[] { "CourseId", "GroupId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeacherAssignmentScopes_OrganizationMemberships_Organizatio~",
                        columns: x => new { x.OrganizationId, x.TeacherId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "ActorId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccessChallenges",
                columns: table => new
                {
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    SecretMac = table.Column<byte[]>(type: "bytea", nullable: false),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TerminalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RecoveryRequestId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessChallenges", x => x.ChallengeId);
                    table.UniqueConstraint("AK_AccessChallenges_ChallengeId_UserId_Purpose", x => new { x.ChallengeId, x.UserId, x.Purpose });
                    table.CheckConstraint("CK_Challenge_Expiry", "\"ExpiresAt\" > \"CreatedAt\" AND ((\"Purpose\" = 'Activation' AND \"ExpiresAt\" <= \"CreatedAt\" + interval '24 hours' AND \"RecoveryRequestId\" IS NULL) OR (\"Purpose\" = 'Recovery' AND \"ExpiresAt\" <= \"CreatedAt\" + interval '15 minutes'))");
                    table.CheckConstraint("CK_Challenge_State", "\"Purpose\" IN ('Activation','Recovery') AND \"State\" IN ('Created','Consumed','Expired','Revoked','Locked') AND \"FailedAttempts\" BETWEEN 0 AND 5 AND \"Version\" > 0 AND octet_length(\"SecretMac\") = 32 AND \"KeyVersion\" > 0");
                    table.CheckConstraint("CK_Challenge_Terminal", "(\"State\" = 'Created' AND \"TerminalAt\" IS NULL AND \"FailedAttempts\" < 5) OR (\"State\" <> 'Created' AND \"TerminalAt\" IS NOT NULL AND \"TerminalAt\" >= \"CreatedAt\")");
                    table.ForeignKey(
                        name: "FK_AccessChallenges_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessChallenges_RecoveryRequests_RecoveryRequestId_UserId",
                        columns: x => new { x.RecoveryRequestId, x.UserId },
                        principalTable: "RecoveryRequests",
                        principalColumns: new[] { "RequestId", "TargetUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChallengeReceipts",
                columns: table => new
                {
                    IssuerActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    PayloadDigest = table.Column<string>(type: "text", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChallengeReceipts", x => new { x.IssuerActorId, x.TargetUserId, x.Purpose, x.IdempotencyKey });
                    table.ForeignKey(
                        name: "FK_ChallengeReceipts_AccessChallenges_ChallengeId_TargetUserId~",
                        columns: x => new { x.ChallengeId, x.TargetUserId, x.Purpose },
                        principalTable: "AccessChallenges",
                        principalColumns: new[] { "ChallengeId", "UserId", "Purpose" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChallengeReceipts_Actors_IssuerActorId",
                        column: x => x.IssuerActorId,
                        principalTable: "Actors",
                        principalColumn: "ActorId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessChallenges_RecoveryRequestId_UserId",
                table: "AccessChallenges",
                columns: new[] { "RecoveryRequestId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessChallenges_UserId_ExpiresAt",
                table: "AccessChallenges",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessChallenges_UserId_Purpose",
                table: "AccessChallenges",
                columns: new[] { "UserId", "Purpose" },
                unique: true,
                filter: "\"State\" = 'Created'");

            migrationBuilder.CreateIndex(
                name: "IX_ActorIdentities_ActorId",
                table: "ActorIdentities",
                column: "ActorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActorIdentities_ProvisioningOrgId",
                table: "ActorIdentities",
                column: "ProvisioningOrgId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentScopes_ContextId_ContextVersion_OrganizationId",
                table: "AttachmentScopes",
                columns: new[] { "ContextId", "ContextVersion", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentScopes_CourseId_OrganizationId",
                table: "AttachmentScopes",
                columns: new[] { "CourseId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttachmentScopes_OrganizationId_UploaderActorId",
                table: "AttachmentScopes",
                columns: new[] { "OrganizationId", "UploaderActorId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeReceipts_ChallengeId_TargetUserId_Purpose",
                table: "ChallengeReceipts",
                columns: new[] { "ChallengeId", "TargetUserId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseContextBindings_ContextId_ContextVersion_Organization~",
                table: "CourseContextBindings",
                columns: new[] { "ContextId", "ContextVersion", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseRegistry_CourseId_OrganizationId",
                table: "CourseRegistry",
                columns: new[] { "CourseId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseRegistry_OrganizationId",
                table: "CourseRegistry",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentScopes_CourseId_GroupId",
                table: "EnrollmentScopes",
                columns: new[] { "CourseId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentScopes_OrganizationId_CourseId_State",
                table: "EnrollmentScopes",
                columns: new[] { "OrganizationId", "CourseId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentScopes_OrganizationId_StudentId",
                table: "EnrollmentScopes",
                columns: new[] { "OrganizationId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentitySessions_UserId_RevokedAt",
                table: "IdentitySessions",
                columns: new[] { "UserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_ActorId_State_OrganizationId",
                table: "OrganizationMemberships",
                columns: new[] { "ActorId", "State", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingContextVersions_OrganizationId",
                table: "ProcessingContextVersions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingRestrictions_ContextId_OriginVersion_Organization~",
                table: "ProcessingRestrictions",
                columns: new[] { "ContextId", "OriginVersion", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingRestrictions_ContextId_State",
                table: "ProcessingRestrictions",
                columns: new[] { "ContextId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingRestrictions_OrganizationId_SubjectActorId",
                table: "ProcessingRestrictions",
                columns: new[] { "OrganizationId", "SubjectActorId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingRestrictions_SubjectActorId_State",
                table: "ProcessingRestrictions",
                columns: new[] { "SubjectActorId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningRequests_UserId",
                table: "ProvisioningRequests",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryRequests_ApproverActorId",
                table: "RecoveryRequests",
                column: "ApproverActorId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryRequests_InitiatingOrgId_InitiatorActorId",
                table: "RecoveryRequests",
                columns: new[] { "InitiatingOrgId", "InitiatorActorId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryRequests_TargetUserId",
                table: "RecoveryRequests",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshVerifiers_SessionId",
                table: "RefreshVerifiers",
                column: "SessionId",
                unique: true,
                filter: "\"ConsumedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAudit_ActorId",
                table: "SecurityAudit",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAudit_ContextId_ContextVersion_OrganizationId",
                table: "SecurityAudit",
                columns: new[] { "ContextId", "ContextVersion", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAudit_OrganizationId_OccurredAt",
                table: "SecurityAudit",
                columns: new[] { "OrganizationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAudit_SubjectRef_OccurredAt",
                table: "SecurityAudit",
                columns: new[] { "SubjectRef", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAssignmentScopes_CourseId_GroupId",
                table: "TeacherAssignmentScopes",
                columns: new[] { "CourseId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAssignmentScopes_OrganizationId_TeacherId",
                table: "TeacherAssignmentScopes",
                columns: new[] { "OrganizationId", "TeacherId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAssignmentScopes_TeacherId_CourseId",
                table: "TeacherAssignmentScopes",
                columns: new[] { "TeacherId", "CourseId" },
                unique: true,
                filter: "\"State\" = 'Active' AND \"GroupId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAssignmentScopes_TeacherId_CourseId_GroupId",
                table: "TeacherAssignmentScopes",
                columns: new[] { "TeacherId", "CourseId", "GroupId" },
                unique: true,
                filter: "\"State\" = 'Active' AND \"GroupId\" IS NOT NULL");
            migrationBuilder.Sql("""
                -- Only new tables reference the registry in M1. Legacy -> registry validation
                -- belongs to a separately authorized M3 after an explicit M2 manifest.
                CREATE EXTENSION IF NOT EXISTS btree_gist;
                ALTER TABLE "EnrollmentScopes" ADD CONSTRAINT "FK_EnrollmentScope_CourseOrganization"
                  FOREIGN KEY ("CourseId", "OrganizationId") REFERENCES "CourseRegistry" ("CourseId", "OrganizationId") ON DELETE RESTRICT;
                ALTER TABLE "TeacherAssignmentScopes" ADD CONSTRAINT "FK_TeacherScope_CourseOrganization"
                  FOREIGN KEY ("CourseId", "OrganizationId") REFERENCES "CourseRegistry" ("CourseId", "OrganizationId") ON DELETE RESTRICT;
                ALTER TABLE "AttachmentScopes" ADD CONSTRAINT "FK_AttachmentScope_CourseOrganization"
                  FOREIGN KEY ("CourseId", "OrganizationId") REFERENCES "CourseRegistry" ("CourseId", "OrganizationId") ON DELETE RESTRICT;
                ALTER TABLE "CourseContextBindings" ADD CONSTRAINT "FK_ContextBinding_CourseOrganization"
                  FOREIGN KEY ("CourseId", "OrganizationId") REFERENCES "CourseRegistry" ("CourseId", "OrganizationId") ON DELETE RESTRICT;
                ALTER TABLE "ProcessingContextVersions" ADD CONSTRAINT "EX_Context_StableOrganization"
                  EXCLUDE USING gist ("ContextId" WITH =, "OrganizationId" WITH <>);
                ALTER TABLE "CourseContextBindings" ADD CONSTRAINT "EX_CourseBinding_NoOverlap"
                  EXCLUDE USING gist ("CourseId" WITH =, tstzrange("EffectiveFrom", "EffectiveTo", '[)') WITH &&);

                CREATE FUNCTION s1_immutable_row() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'L1 immutable record: %', TG_TABLE_NAME USING ERRCODE = '23514';
                END $$;
                CREATE TRIGGER s1_context_immutable BEFORE UPDATE OR DELETE ON "ProcessingContextVersions"
                  FOR EACH ROW EXECUTE FUNCTION s1_immutable_row();
                CREATE TRIGGER s1_audit_immutable BEFORE UPDATE OR DELETE ON "SecurityAudit"
                  FOR EACH ROW EXECUTE FUNCTION s1_immutable_row();
                CREATE TRIGGER s1_receipt_immutable BEFORE UPDATE OR DELETE ON "ChallengeReceipts"
                  FOR EACH ROW EXECUTE FUNCTION s1_immutable_row();

                -- Retain stable roots / association keys even when currently unreferenced.
                CREATE FUNCTION s1_stable_key() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE col text;
                BEGIN
                  IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'L1 root retained' USING ERRCODE = '23514'; END IF;
                  FOREACH col IN ARRAY TG_ARGV LOOP
                    IF to_jsonb(NEW)->col IS DISTINCT FROM to_jsonb(OLD)->col THEN
                      RAISE EXCEPTION 'L1 stable association: %', col USING ERRCODE = '23514';
                    END IF;
                  END LOOP;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER s1_actor_key BEFORE UPDATE OR DELETE ON "Actors" FOR EACH ROW EXECUTE FUNCTION s1_stable_key('ActorId');
                CREATE TRIGGER s1_identity_key BEFORE UPDATE OR DELETE ON "ActorIdentities" FOR EACH ROW EXECUTE FUNCTION s1_stable_key('UserId','ActorId','ProvisioningOrgId');
                CREATE TRIGGER s1_membership_key BEFORE UPDATE OR DELETE ON "OrganizationMemberships" FOR EACH ROW EXECUTE FUNCTION s1_stable_key('OrganizationId','ActorId');
                CREATE TRIGGER s1_enrollment_key BEFORE UPDATE OR DELETE ON "EnrollmentScopes" FOR EACH ROW EXECUTE FUNCTION s1_stable_key('StudentId','CourseId','OrganizationId','GroupId');
                CREATE TRIGGER s1_teacher_key BEFORE UPDATE OR DELETE ON "TeacherAssignmentScopes" FOR EACH ROW EXECUTE FUNCTION s1_stable_key('GrantId','TeacherId','CourseId','GroupId','OrganizationId');
                CREATE TRIGGER s1_attachment_key BEFORE UPDATE OR DELETE ON "AttachmentScopes" FOR EACH ROW EXECUTE FUNCTION s1_stable_key('AttachmentId','OrganizationId','CourseId','UploaderActorId','ContextId','ContextVersion');

                CREATE FUNCTION s1_course_owner() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' OR NEW."CourseId" <> OLD."CourseId" OR
                    (OLD."MappingState" = 'Verified' AND NEW IS DISTINCT FROM OLD) THEN
                    RAISE EXCEPTION 'L1 course ownership is stable' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER s1_course_owner BEFORE UPDATE OR DELETE ON "CourseRegistry" FOR EACH ROW EXECUTE FUNCTION s1_course_owner();

                CREATE FUNCTION s1_binding_history() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' OR (to_jsonb(NEW) - 'EffectiveTo') IS DISTINCT FROM (to_jsonb(OLD) - 'EffectiveTo')
                    OR (OLD."EffectiveTo" IS NOT NULL AND NEW."EffectiveTo" IS DISTINCT FROM OLD."EffectiveTo") THEN
                    RAISE EXCEPTION 'L1 binding history retained; only open interval closure is allowed' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER s1_binding_history BEFORE UPDATE OR DELETE ON "CourseContextBindings" FOR EACH ROW EXECUTE FUNCTION s1_binding_history();

                CREATE FUNCTION s1_grant_generation() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE oldgen bigint; newgen bigint;
                BEGIN
                  oldgen := (to_jsonb(OLD)->>TG_ARGV[0])::bigint;
                  newgen := (to_jsonb(NEW)->>TG_ARGV[0])::bigint;
                  IF NEW."Version" <= OLD."Version" OR newgen < oldgen OR
                     (newgen = oldgen AND ((OLD."State" = 'Revoked' AND NEW."State" = 'Active') OR
                       (OLD."AccessValidUntil" IS NOT NULL AND (NEW."AccessValidUntil" IS NULL OR NEW."AccessValidUntil" > OLD."AccessValidUntil")) OR
                       (OLD."RevocationRequestedAt" IS NOT NULL AND NEW."RevocationRequestedAt" IS DISTINCT FROM OLD."RevocationRequestedAt"))) THEN
                    RAISE EXCEPTION 'L1 regrant requires new generation; revoke deadline cannot move' USING ERRCODE = '23514';
                  END IF;
                  IF TG_TABLE_NAME <> 'OrganizationMemberships' AND newgen = oldgen AND
                     to_jsonb(NEW)->'MembershipGeneration' IS DISTINCT FROM to_jsonb(OLD)->'MembershipGeneration' THEN
                    RAISE EXCEPTION 'L1 rebind requires new grant generation' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER s1_membership_generation BEFORE UPDATE ON "OrganizationMemberships" FOR EACH ROW EXECUTE FUNCTION s1_grant_generation('Generation');
                CREATE TRIGGER s1_enrollment_generation BEFORE UPDATE ON "EnrollmentScopes" FOR EACH ROW EXECUTE FUNCTION s1_grant_generation('EnrollmentGeneration');
                CREATE TRIGGER s1_teacher_generation BEFORE UPDATE ON "TeacherAssignmentScopes" FOR EACH ROW EXECUTE FUNCTION s1_grant_generation('Generation');

                -- Sidecar association checks lock the corresponding legacy row. Reverse guards
                -- reject later mutation of its resource/actor/group, including null whole-course.
                CREATE FUNCTION s1_scope_matches_legacy() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE enrollment "AccessEnrollments"%ROWTYPE; teacher "AccessTeachers"%ROWTYPE;
                BEGIN
                  IF TG_TABLE_NAME = 'EnrollmentScopes' THEN
                    UPDATE "AccessEnrollments" SET "IsActive"="IsActive" WHERE "StudentId" = NEW."StudentId" AND "CourseId" = NEW."CourseId" RETURNING * INTO enrollment;
                    IF NOT FOUND OR enrollment."GroupId" IS DISTINCT FROM NEW."GroupId" THEN
                      RAISE EXCEPTION 'L1 enrollment sidecar mismatch' USING ERRCODE = '23514';
                    END IF;
                  ELSE
                    UPDATE "AccessTeachers" SET "TeacherId"="TeacherId" WHERE "Id" = NEW."GrantId" RETURNING * INTO teacher;
                    IF NOT FOUND OR (teacher."TeacherId",teacher."CourseId",teacher."GroupId") IS DISTINCT FROM (NEW."TeacherId",NEW."CourseId",NEW."GroupId") THEN
                      RAISE EXCEPTION 'L1 teacher sidecar mismatch' USING ERRCODE = '23514';
                    END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER s1_enrollment_match BEFORE INSERT OR UPDATE ON "EnrollmentScopes" FOR EACH ROW EXECUTE FUNCTION s1_scope_matches_legacy();
                CREATE TRIGGER s1_teacher_match BEFORE INSERT OR UPDATE ON "TeacherAssignmentScopes" FOR EACH ROW EXECUTE FUNCTION s1_scope_matches_legacy();
                CREATE FUNCTION s1_legacy_scope_stable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_TABLE_NAME = 'AccessEnrollments' THEN
                    IF EXISTS (SELECT 1 FROM "EnrollmentScopes" WHERE "StudentId" = OLD."StudentId" AND "CourseId" = OLD."CourseId") AND
                      (NEW."StudentId",NEW."CourseId",NEW."GroupId") IS DISTINCT FROM (OLD."StudentId",OLD."CourseId",OLD."GroupId") THEN
                      RAISE EXCEPTION 'L1 scoped enrollment binding is stable' USING ERRCODE = '23514';
                    END IF;
                  ELSE
                    IF EXISTS (SELECT 1 FROM "TeacherAssignmentScopes" WHERE "GrantId" = OLD."Id") AND
                      (NEW."Id",NEW."TeacherId",NEW."CourseId",NEW."GroupId") IS DISTINCT FROM (OLD."Id",OLD."TeacherId",OLD."CourseId",OLD."GroupId") THEN
                      RAISE EXCEPTION 'L1 scoped teacher binding is stable' USING ERRCODE = '23514';
                    END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER s1_legacy_enrollment BEFORE UPDATE ON "AccessEnrollments" FOR EACH ROW EXECUTE FUNCTION s1_legacy_scope_stable();
                CREATE TRIGGER s1_legacy_teacher BEFORE UPDATE ON "AccessTeachers" FOR EACH ROW EXECUTE FUNCTION s1_legacy_scope_stable();

                -- Called for both adding a sidecar and claiming the stored original. Legacy rows
                -- without a sidecar stay untouched; S1 does not expose them to a new auth path.
                CREATE FUNCTION s1_check_attachment(attachment_id uuid, course_id text, organization_id uuid, uploader uuid, submission_id uuid, message_id uuid, stored_owner uuid) RETURNS void LANGUAGE plpgsql AS $$
                DECLARE parent_course text; parent_actor uuid;
                BEGIN
                  IF uploader <> stored_owner THEN RAISE EXCEPTION 'L1 uploader mismatch' USING ERRCODE = '23514'; END IF;
                  IF submission_id IS NOT NULL THEN
                    SELECT a."CourseId",s."SubmittedBy" INTO parent_course,parent_actor FROM "Submissions" s
                      JOIN "ProblemWorks" w ON w."Id"=s."ProblemWorkId" JOIN "ProblemAssignments" a ON a."Id"=w."ProblemAssignmentId"
                      WHERE s."Id"=submission_id AND attachment_id=ANY(s."AttachmentIds");
                  ELSIF message_id IS NOT NULL THEN
                    SELECT a."CourseId",m."AuthorId" INTO parent_course,parent_actor FROM "Messages" m
                      JOIN "ProblemWorks" w ON w."Id"=m."ProblemWorkId" JOIN "ProblemAssignments" a ON a."Id"=w."ProblemAssignmentId"
                      WHERE m."Id"=message_id AND attachment_id=ANY(m."AttachmentIds");
                  ELSE RETURN;
                  END IF;
                  IF parent_course IS DISTINCT FROM course_id OR parent_actor IS DISTINCT FROM uploader OR NOT EXISTS
                    (SELECT 1 FROM "CourseRegistry" WHERE "CourseId"=parent_course AND "OrganizationId"=organization_id AND "MappingState"='Verified') THEN
                    RAISE EXCEPTION 'L1 attachment parent scope mismatch' USING ERRCODE = '23514';
                  END IF;
                END $$;
                CREATE FUNCTION s1_attachment_scope_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE stored "StoredAttachments"%ROWTYPE;
                BEGIN
                  UPDATE "StoredAttachments" SET "OwnerId"="OwnerId" WHERE "Id"=NEW."AttachmentId" RETURNING * INTO stored;
                  IF NOT FOUND THEN RAISE EXCEPTION 'L1 attachment missing' USING ERRCODE = '23503'; END IF;
                  PERFORM s1_check_attachment(NEW."AttachmentId",NEW."CourseId",NEW."OrganizationId",NEW."UploaderActorId",stored."SubmissionId",stored."MessageId",stored."OwnerId");
                  RETURN NEW;
                END $$;
                CREATE TRIGGER s1_attachment_scope BEFORE INSERT OR UPDATE ON "AttachmentScopes" FOR EACH ROW EXECUTE FUNCTION s1_attachment_scope_guard();
                CREATE FUNCTION s1_stored_scope_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE scope "AttachmentScopes"%ROWTYPE;
                BEGIN
                  SELECT * INTO scope FROM "AttachmentScopes" WHERE "AttachmentId"=NEW."Id";
                  IF FOUND THEN
                    IF scope."Quarantined" AND (NEW."SubmissionId" IS NOT NULL OR NEW."MessageId" IS NOT NULL) THEN
                      RAISE EXCEPTION 'L1 quarantined attachment' USING ERRCODE = '23514';
                    END IF;
                    PERFORM s1_check_attachment(NEW."Id",scope."CourseId",scope."OrganizationId",scope."UploaderActorId",NEW."SubmissionId",NEW."MessageId",NEW."OwnerId");
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER s1_stored_scope BEFORE UPDATE ON "StoredAttachments" FOR EACH ROW EXECUTE FUNCTION s1_stored_scope_guard();

                CREATE FUNCTION s1_challenge_terminal() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' OR OLD."State" <> 'Created' OR NEW."Version" <= OLD."Version" OR NEW."FailedAttempts" < OLD."FailedAttempts" OR
                    (NEW."ChallengeId",NEW."UserId",NEW."Purpose",NEW."SecretMac",NEW."KeyVersion",NEW."CreatedAt",NEW."ExpiresAt",NEW."RecoveryRequestId") IS DISTINCT FROM
                    (OLD."ChallengeId",OLD."UserId",OLD."Purpose",OLD."SecretMac",OLD."KeyVersion",OLD."CreatedAt",OLD."ExpiresAt",OLD."RecoveryRequestId") THEN
                    RAISE EXCEPTION 'L1 challenge is terminal or binding changed' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER s1_challenge_terminal BEFORE UPDATE OR DELETE ON "AccessChallenges" FOR EACH ROW EXECUTE FUNCTION s1_challenge_terminal();
                CREATE FUNCTION s1_session_monotone() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' OR NEW."Version" <= OLD."Version" OR NEW."Generation" < OLD."Generation" OR
                    NEW."LastSuccessfulRefreshAt" < OLD."LastSuccessfulRefreshAt" OR
                    (NEW."SessionId",NEW."UserId",NEW."CreatedAt",NEW."AbsoluteExpiresAt") IS DISTINCT FROM (OLD."SessionId",OLD."UserId",OLD."CreatedAt",OLD."AbsoluteExpiresAt") OR
                    (OLD."RevokedAt" IS NOT NULL AND NEW IS DISTINCT FROM OLD) THEN
                    RAISE EXCEPTION 'L1 session cannot resurrect' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER s1_session_monotone BEFORE UPDATE OR DELETE ON "IdentitySessions" FOR EACH ROW EXECUTE FUNCTION s1_session_monotone();
                CREATE FUNCTION s1_refresh_history() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' OR OLD."ConsumedAt" IS NOT NULL OR NEW."ConsumedAt" IS NULL OR
                    (to_jsonb(NEW)-'ConsumedAt') IS DISTINCT FROM (to_jsonb(OLD)-'ConsumedAt') THEN
                    RAISE EXCEPTION 'L1 refresh verifier history retained' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER s1_refresh_history BEFORE UPDATE OR DELETE ON "RefreshVerifiers" FOR EACH ROW EXECUTE FUNCTION s1_refresh_history();
                -- User creation and stable actor mapping must commit together. Historical actors
                -- may exist without credentials; an Identity user may not commit without a map.
                ALTER TABLE "AspNetUsers" ADD CONSTRAINT "FK_User_StableActorIdentity"
                  FOREIGN KEY ("Id") REFERENCES "ActorIdentities" ("UserId")
                  ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;

                CREATE FUNCTION s1_binding_effective() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM "ProcessingContextVersions" WHERE "ContextId"=NEW."ContextId" AND "Version"=NEW."ContextVersion" AND "EffectiveFrom">NEW."EffectiveFrom") THEN
                    RAISE EXCEPTION 'L1 context is not yet effective' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER s1_binding_effective BEFORE INSERT OR UPDATE ON "CourseContextBindings" FOR EACH ROW EXECUTE FUNCTION s1_binding_effective();
                CREATE TRIGGER s1_restriction_provenance BEFORE UPDATE OR DELETE ON "ProcessingRestrictions" FOR EACH ROW EXECUTE FUNCTION s1_stable_key('RestrictionId','ContextId','OriginVersion','OrganizationId','SubjectActorId','ResourceRef','Operations');

                CREATE FUNCTION s1_security_monotone() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' OR NEW."UserId" <> OLD."UserId" OR NEW."Version" <= OLD."Version" OR NEW."CredentialEpoch" < OLD."CredentialEpoch" OR NEW."ChangedAt" < OLD."ChangedAt" THEN
                    RAISE EXCEPTION 'L1 security version cannot regress' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER s1_security_monotone BEFORE UPDATE OR DELETE ON "AccountSecurityState" FOR EACH ROW EXECUTE FUNCTION s1_security_monotone();

                CREATE FUNCTION s1_recovery_history() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' OR NEW."Version" <= OLD."Version" OR
                    (NEW."RequestId",NEW."TargetUserId",NEW."InitiatingOrgId",NEW."InitiatorActorId") IS DISTINCT FROM
                    (OLD."RequestId",OLD."TargetUserId",OLD."InitiatingOrgId",OLD."InitiatorActorId") OR
                    NOT ((OLD."State"='Requested' AND NEW."State" IN ('Approved','Rejected','Suspended')) OR
                         (OLD."State"='Approved' AND NEW."State" IN ('Fulfilled','Cancelled') AND
                          (NEW."ApproverActorId",NEW."ApprovedAt",NEW."VerificationRef") IS NOT DISTINCT FROM (OLD."ApproverActorId",OLD."ApprovedAt",OLD."VerificationRef"))) THEN
                    RAISE EXCEPTION 'L1 recovery terminal state or binding cannot change' USING ERRCODE = '23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER s1_recovery_history BEFORE UPDATE OR DELETE ON "RecoveryRequests" FOR EACH ROW EXECUTE FUNCTION s1_recovery_history();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("M1 retains identity, scope and security history. Use an approved restore or forward migration; destructive Down is prohibited.");
    }
}
