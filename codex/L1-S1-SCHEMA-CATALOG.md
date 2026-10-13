# S1 M1 — Schema catalog

Source: `20261009094756_SchemaFoundationsM1.cs`. Generated from the reviewed migration; PostgreSQL tests validate the actual schema.

27 new tables; 27 primary keys; 41 EF foreign keys + 5 SQL foreign keys; 29 CHECK constraints; 42 explicit indexes; 2 exclusion constraints. All foreign-key deletes are RESTRICT.

## Tables and primary keys

| Table | PK |
|---|---|
| Actors | `PK_Actors` |
| AspNetRoles | `PK_AspNetRoles` |
| AspNetUsers | `PK_AspNetUsers` |
| Organizations | `PK_Organizations` |
| AspNetRoleClaims | `PK_AspNetRoleClaims` |
| AccountSecurityState | `PK_AccountSecurityState` |
| AspNetUserClaims | `PK_AspNetUserClaims` |
| AspNetUserLogins | `PK_AspNetUserLogins` |
| AspNetUserRoles | `PK_AspNetUserRoles` |
| AspNetUserTokens | `PK_AspNetUserTokens` |
| IdentitySessions | `PK_IdentitySessions` |
| ActorIdentities | `PK_ActorIdentities` |
| CourseRegistry | `PK_CourseRegistry` |
| OrganizationMemberships | `PK_OrganizationMemberships` |
| ProcessingContextVersions | `PK_ProcessingContextVersions` |
| ProvisioningRequests | `PK_ProvisioningRequests` |
| RefreshVerifiers | `PK_RefreshVerifiers` |
| CourseGroups | `PK_CourseGroups` |
| RecoveryRequests | `PK_RecoveryRequests` |
| AttachmentScopes | `PK_AttachmentScopes` |
| CourseContextBindings | `PK_CourseContextBindings` |
| ProcessingRestrictions | `PK_ProcessingRestrictions` |
| SecurityAudit | `PK_SecurityAudit` |
| EnrollmentScopes | `PK_EnrollmentScopes` |
| TeacherAssignmentScopes | `PK_TeacherAssignmentScopes` |
| AccessChallenges | `PK_AccessChallenges` |
| ChallengeReceipts | `PK_ChallengeReceipts` |

## EF foreign keys

| Table | Constraint |
|---|---|
| AspNetRoleClaims | `FK_AspNetRoleClaims_AspNetRoles_RoleId` |
| AccountSecurityState | `FK_AccountSecurityState_AspNetUsers_UserId` |
| AspNetUserClaims | `FK_AspNetUserClaims_AspNetUsers_UserId` |
| AspNetUserLogins | `FK_AspNetUserLogins_AspNetUsers_UserId` |
| AspNetUserRoles | `FK_AspNetUserRoles_AspNetRoles_RoleId` |
| AspNetUserRoles | `FK_AspNetUserRoles_AspNetUsers_UserId` |
| AspNetUserTokens | `FK_AspNetUserTokens_AspNetUsers_UserId` |
| IdentitySessions | `FK_IdentitySessions_AspNetUsers_UserId` |
| ActorIdentities | `FK_ActorIdentities_Actors_ActorId` |
| ActorIdentities | `FK_ActorIdentities_AspNetUsers_UserId` |
| ActorIdentities | `FK_ActorIdentities_Organizations_ProvisioningOrgId` |
| CourseRegistry | `FK_CourseRegistry_Organizations_OrganizationId` |
| OrganizationMemberships | `FK_OrganizationMemberships_Actors_ActorId` |
| OrganizationMemberships | `FK_OrganizationMemberships_Organizations_OrganizationId` |
| ProcessingContextVersions | `FK_ProcessingContextVersions_Organizations_OrganizationId` |
| ProvisioningRequests | `FK_ProvisioningRequests_AspNetUsers_UserId` |
| ProvisioningRequests | `FK_ProvisioningRequests_Organizations_OrganizationId` |
| RefreshVerifiers | `FK_RefreshVerifiers_IdentitySessions_SessionId` |
| CourseGroups | `FK_CourseGroups_CourseRegistry_CourseId` |
| RecoveryRequests | `FK_RecoveryRequests_Actors_ApproverActorId` |
| RecoveryRequests | `FK_RecoveryRequests_AspNetUsers_TargetUserId` |
| RecoveryRequests | `FK_RecoveryRequests_OrganizationMemberships_InitiatingOrgId_In~` |
| AttachmentScopes | `FK_AttachmentScopes_OrganizationMemberships_OrganizationId_Upl~` |
| AttachmentScopes | `FK_AttachmentScopes_ProcessingContextVersions_ContextId_Contex~` |
| AttachmentScopes | `FK_AttachmentScopes_StoredAttachments_AttachmentId` |
| CourseContextBindings | `FK_CourseContextBindings_ProcessingContextVersions_ContextId_C~` |
| ProcessingRestrictions | `FK_ProcessingRestrictions_OrganizationMemberships_Organization~` |
| ProcessingRestrictions | `FK_ProcessingRestrictions_ProcessingContextVersions_ContextId_~` |
| SecurityAudit | `FK_SecurityAudit_Actors_ActorId` |
| SecurityAudit | `FK_SecurityAudit_Organizations_OrganizationId` |
| SecurityAudit | `FK_SecurityAudit_ProcessingContextVersions_ContextId_ContextVe~` |
| EnrollmentScopes | `FK_EnrollmentScopes_AccessEnrollments_StudentId_CourseId` |
| EnrollmentScopes | `FK_EnrollmentScopes_CourseGroups_CourseId_GroupId` |
| EnrollmentScopes | `FK_EnrollmentScopes_OrganizationMemberships_OrganizationId_Stu~` |
| TeacherAssignmentScopes | `FK_TeacherAssignmentScopes_AccessTeachers_GrantId` |
| TeacherAssignmentScopes | `FK_TeacherAssignmentScopes_CourseGroups_CourseId_GroupId` |
| TeacherAssignmentScopes | `FK_TeacherAssignmentScopes_OrganizationMemberships_Organizatio~` |
| AccessChallenges | `FK_AccessChallenges_AspNetUsers_UserId` |
| AccessChallenges | `FK_AccessChallenges_RecoveryRequests_RecoveryRequestId_UserId` |
| ChallengeReceipts | `FK_ChallengeReceipts_AccessChallenges_ChallengeId_TargetUserId~` |
| ChallengeReceipts | `FK_ChallengeReceipts_Actors_IssuerActorId` |

## SQL constraints (nullable registry, deferred mapping, exclusion)

| Table | Constraint |
|---|---|
| EnrollmentScopes | `FK_EnrollmentScope_CourseOrganization` |
| TeacherAssignmentScopes | `FK_TeacherScope_CourseOrganization` |
| AttachmentScopes | `FK_AttachmentScope_CourseOrganization` |
| CourseContextBindings | `FK_ContextBinding_CourseOrganization` |
| ProcessingContextVersions | `EX_Context_StableOrganization` |
| CourseContextBindings | `EX_CourseBinding_NoOverlap` |
| AspNetUsers | `FK_User_StableActorIdentity` |

## CHECK constraints

| Table | Name | Predicate |
|---|---|---|
| Actors | `CK_Actor_Role` | `"PrimaryEducationalRole" IS NULL OR "PrimaryEducationalRole" IN ('Student','Teacher')` |
| Organizations | `CK_Organization_State` | `"State" IN ('Enabled','Suspended')` |
| Organizations | `CK_Organization_Synthetic` | `"SyntheticOnly"` |
| AccountSecurityState | `CK_Security_State` | `"State" IN ('Enabled','SecurityBlocked') AND "CredentialEpoch" > 0 AND "Version" > 0` |
| IdentitySessions | `CK_Session_Times` | `"Generation" > 0 AND "Version" > 0 AND "AbsoluteExpiresAt" = "CreatedAt" + interval '12 hours' AND "LastSuccessfulRefreshAt" >= "CreatedAt" AND "LastSuccessfulRefreshAt" <= "AbsoluteExpiresAt" AND ("RevokedAt" IS NULL OR "RevokedAt" >= "CreatedAt")` |
| CourseRegistry | `CK_Course_Mapping` | `("MappingState" = 'Verified' AND "OrganizationId" IS NOT NULL) OR ("MappingState" = 'Unknown' AND "OrganizationId" IS NULL)` |
| OrganizationMemberships | `CK_Membership_Permissions` | `"PermissionFlags" BETWEEN 0 AND 15` |
| OrganizationMemberships | `CK_Membership_Role` | `"EducationalRole" IS NULL OR "EducationalRole" IN ('Student','Teacher')` |
| OrganizationMemberships | `CK_OrganizationMembership_Deadline` | `("RevocationRequestedAt" IS NULL AND "AccessValidUntil" IS NULL AND "State" = 'Active') OR ("RevocationRequestedAt" IS NOT NULL AND "AccessValidUntil" IS NOT NULL AND "State" = 'Revoked' AND "AccessValidUntil" <= "RevocationRequestedAt" + interval '2 hours')` |
| OrganizationMemberships | `CK_OrganizationMembership_Generation` | `"Generation" > 0 AND "Version" > 0` |
| OrganizationMemberships | `CK_OrganizationMembership_State` | `"State" IN ('Active','Revoked')` |
| ProcessingContextVersions | `CK_Context_Synthetic` | `"SyntheticOnly" AND "ApprovalState" IN ('Unreviewed','SyntheticOnly') AND "LegalBasisRef" IS NULL` |
| ProcessingContextVersions | `CK_Context_Version` | `"Version" > 0` |
| ProvisioningRequests | `CK_Provision_State` | `("State" = 'Pending' AND "UserId" IS NULL) OR ("State" = 'Completed' AND "UserId" IS NOT NULL)` |
| RefreshVerifiers | `CK_Refresh_Verifier` | `"Generation" > 0 AND octet_length("Verifier") = 32 AND "KeyVersion" > 0 AND ("ConsumedAt" IS NULL OR "ConsumedAt" >= "CreatedAt")` |
| RecoveryRequests | `CK_Recovery_Approval` | `("ApproverActorId" IS NULL AND "ApprovedAt" IS NULL AND "State" NOT IN ('Approved','Fulfilled')) OR ("ApproverActorId" IS NOT NULL AND "ApproverActorId" <> "InitiatorActorId" AND "ApprovedAt" IS NOT NULL AND "VerificationRef" IS NOT NULL)` |
| RecoveryRequests | `CK_Recovery_State` | `"State" IN ('Requested','Approved','Rejected','Suspended','Fulfilled','Cancelled') AND "Version" > 0` |
| CourseContextBindings | `CK_Binding_Interval` | `"EffectiveTo" IS NULL OR "EffectiveTo" > "EffectiveFrom"` |
| ProcessingRestrictions | `CK_Restriction_State` | `"State" IN ('Active','Withdrawn') AND "Version" > 0 AND "Operations" > 0 AND "Operations" <= 31` |
| SecurityAudit | `CK_Audit_Context` | `("ContextId" IS NULL AND "ContextVersion" IS NULL) OR ("ContextId" IS NOT NULL AND "ContextVersion" IS NOT NULL AND "OrganizationId" IS NOT NULL)` |
| EnrollmentScopes | `CK_EnrollmentScope_Deadline` | `("RevocationRequestedAt" IS NULL AND "AccessValidUntil" IS NULL AND "State" = 'Active') OR ("RevocationRequestedAt" IS NOT NULL AND "AccessValidUntil" IS NOT NULL AND "State" = 'Revoked' AND "AccessValidUntil" <= "RevocationRequestedAt" + interval '2 hours')` |
| EnrollmentScopes | `CK_EnrollmentScope_Generation` | `"EnrollmentGeneration" > 0 AND "Version" > 0 AND "MembershipGeneration" > 0` |
| EnrollmentScopes | `CK_EnrollmentScope_State` | `"State" IN ('Active','Revoked')` |
| TeacherAssignmentScopes | `CK_TeacherAssignmentScope_Deadline` | `("RevocationRequestedAt" IS NULL AND "AccessValidUntil" IS NULL AND "State" = 'Active') OR ("RevocationRequestedAt" IS NOT NULL AND "AccessValidUntil" IS NOT NULL AND "State" = 'Revoked' AND "AccessValidUntil" <= "RevocationRequestedAt" + interval '2 hours')` |
| TeacherAssignmentScopes | `CK_TeacherAssignmentScope_Generation` | `"Generation" > 0 AND "Version" > 0 AND "MembershipGeneration" > 0` |
| TeacherAssignmentScopes | `CK_TeacherAssignmentScope_State` | `"State" IN ('Active','Revoked')` |
| AccessChallenges | `CK_Challenge_Expiry` | `"ExpiresAt" > "CreatedAt" AND (("Purpose" = 'Activation' AND "ExpiresAt" <= "CreatedAt" + interval '24 hours' AND "RecoveryRequestId" IS NULL) OR ("Purpose" = 'Recovery' AND "ExpiresAt" <= "CreatedAt" + interval '15 minutes'))` |
| AccessChallenges | `CK_Challenge_State` | `"Purpose" IN ('Activation','Recovery') AND "State" IN ('Created','Consumed','Expired','Revoked','Locked') AND "FailedAttempts" BETWEEN 0 AND 5 AND "Version" > 0 AND octet_length("SecretMac") = 32 AND "KeyVersion" > 0` |
| AccessChallenges | `CK_Challenge_Terminal` | `("State" = 'Created' AND "TerminalAt" IS NULL AND "FailedAttempts" < 5) OR ("State" <> 'Created' AND "TerminalAt" IS NOT NULL AND "TerminalAt" >= "CreatedAt")` |

## Explicit indexes

| Table | Index | Kind |
|---|---|---|
| AccessChallenges | `IX_AccessChallenges_RecoveryRequestId_UserId` | INDEX |
| AccessChallenges | `IX_AccessChallenges_UserId_ExpiresAt` | INDEX |
| AccessChallenges | `IX_AccessChallenges_UserId_Purpose` | UNIQUE |
| ActorIdentities | `IX_ActorIdentities_ActorId` | UNIQUE |
| ActorIdentities | `IX_ActorIdentities_ProvisioningOrgId` | INDEX |
| AspNetRoleClaims | `IX_AspNetRoleClaims_RoleId` | INDEX |
| AspNetRoles | `RoleNameIndex` | UNIQUE |
| AspNetUserClaims | `IX_AspNetUserClaims_UserId` | INDEX |
| AspNetUserLogins | `IX_AspNetUserLogins_UserId` | INDEX |
| AspNetUserRoles | `IX_AspNetUserRoles_RoleId` | INDEX |
| AspNetUsers | `EmailIndex` | INDEX |
| AspNetUsers | `UserNameIndex` | UNIQUE |
| AttachmentScopes | `IX_AttachmentScopes_ContextId_ContextVersion_OrganizationId` | INDEX |
| AttachmentScopes | `IX_AttachmentScopes_CourseId_OrganizationId` | INDEX |
| AttachmentScopes | `IX_AttachmentScopes_OrganizationId_UploaderActorId` | INDEX |
| ChallengeReceipts | `IX_ChallengeReceipts_ChallengeId_TargetUserId_Purpose` | INDEX |
| CourseContextBindings | `IX_CourseContextBindings_ContextId_ContextVersion_Organization~` | INDEX |
| CourseRegistry | `IX_CourseRegistry_CourseId_OrganizationId` | UNIQUE |
| CourseRegistry | `IX_CourseRegistry_OrganizationId` | INDEX |
| EnrollmentScopes | `IX_EnrollmentScopes_CourseId_GroupId` | INDEX |
| EnrollmentScopes | `IX_EnrollmentScopes_OrganizationId_CourseId_State` | INDEX |
| EnrollmentScopes | `IX_EnrollmentScopes_OrganizationId_StudentId` | INDEX |
| IdentitySessions | `IX_IdentitySessions_UserId_RevokedAt` | INDEX |
| OrganizationMemberships | `IX_OrganizationMemberships_ActorId_State_OrganizationId` | INDEX |
| ProcessingContextVersions | `IX_ProcessingContextVersions_OrganizationId` | INDEX |
| ProcessingRestrictions | `IX_ProcessingRestrictions_ContextId_OriginVersion_Organization~` | INDEX |
| ProcessingRestrictions | `IX_ProcessingRestrictions_ContextId_State` | INDEX |
| ProcessingRestrictions | `IX_ProcessingRestrictions_OrganizationId_SubjectActorId` | INDEX |
| ProcessingRestrictions | `IX_ProcessingRestrictions_SubjectActorId_State` | INDEX |
| ProvisioningRequests | `IX_ProvisioningRequests_UserId` | INDEX |
| RecoveryRequests | `IX_RecoveryRequests_ApproverActorId` | INDEX |
| RecoveryRequests | `IX_RecoveryRequests_InitiatingOrgId_InitiatorActorId` | INDEX |
| RecoveryRequests | `IX_RecoveryRequests_TargetUserId` | INDEX |
| RefreshVerifiers | `IX_RefreshVerifiers_SessionId` | UNIQUE |
| SecurityAudit | `IX_SecurityAudit_ActorId` | INDEX |
| SecurityAudit | `IX_SecurityAudit_ContextId_ContextVersion_OrganizationId` | INDEX |
| SecurityAudit | `IX_SecurityAudit_OrganizationId_OccurredAt` | INDEX |
| SecurityAudit | `IX_SecurityAudit_SubjectRef_OccurredAt` | INDEX |
| TeacherAssignmentScopes | `IX_TeacherAssignmentScopes_CourseId_GroupId` | INDEX |
| TeacherAssignmentScopes | `IX_TeacherAssignmentScopes_OrganizationId_TeacherId` | INDEX |
| TeacherAssignmentScopes | `IX_TeacherAssignmentScopes_TeacherId_CourseId` | UNIQUE |
| TeacherAssignmentScopes | `IX_TeacherAssignmentScopes_TeacherId_CourseId_GroupId` | UNIQUE |

Partial unique predicates: teacher `(TeacherId, CourseId, GroupId)` for Active group grants and `(TeacherId, CourseId)` for Active whole-course grants; challenge `(UserId, Purpose)` for Created; refresh verifier `SessionId` for unconsumed. Standard Identity username/role normalized uniqueness is retained. PK and alternate keys additionally create PostgreSQL backing indexes.

## Triggers

| Table | Trigger | Timing |
|---|---|---|
| ProcessingContextVersions | `s1_context_immutable` | BEFORE UPDATE OR DELETE |
| SecurityAudit | `s1_audit_immutable` | BEFORE UPDATE OR DELETE |
| ChallengeReceipts | `s1_receipt_immutable` | BEFORE UPDATE OR DELETE |
| Actors | `s1_actor_key` | BEFORE UPDATE OR DELETE |
| ActorIdentities | `s1_identity_key` | BEFORE UPDATE OR DELETE |
| OrganizationMemberships | `s1_membership_key` | BEFORE UPDATE OR DELETE |
| EnrollmentScopes | `s1_enrollment_key` | BEFORE UPDATE OR DELETE |
| TeacherAssignmentScopes | `s1_teacher_key` | BEFORE UPDATE OR DELETE |
| AttachmentScopes | `s1_attachment_key` | BEFORE UPDATE OR DELETE |
| CourseRegistry | `s1_course_owner` | BEFORE UPDATE OR DELETE |
| CourseContextBindings | `s1_binding_history` | BEFORE UPDATE OR DELETE |
| OrganizationMemberships | `s1_membership_generation` | BEFORE UPDATE |
| EnrollmentScopes | `s1_enrollment_generation` | BEFORE UPDATE |
| TeacherAssignmentScopes | `s1_teacher_generation` | BEFORE UPDATE |
| EnrollmentScopes | `s1_enrollment_match` | BEFORE INSERT OR UPDATE |
| TeacherAssignmentScopes | `s1_teacher_match` | BEFORE INSERT OR UPDATE |
| AccessEnrollments | `s1_legacy_enrollment` | BEFORE UPDATE |
| AccessTeachers | `s1_legacy_teacher` | BEFORE UPDATE |
| AttachmentScopes | `s1_attachment_scope` | BEFORE INSERT OR UPDATE |
| StoredAttachments | `s1_stored_scope` | BEFORE UPDATE |
| AccessChallenges | `s1_challenge_terminal` | BEFORE UPDATE OR DELETE |
| IdentitySessions | `s1_session_monotone` | BEFORE UPDATE OR DELETE |
| RefreshVerifiers | `s1_refresh_history` | BEFORE UPDATE OR DELETE |
| CourseContextBindings | `s1_binding_effective` | BEFORE INSERT OR UPDATE |
| ProcessingRestrictions | `s1_restriction_provenance` | BEFORE UPDATE OR DELETE |
| AccountSecurityState | `s1_security_monotone` | BEFORE UPDATE OR DELETE |
| RecoveryRequests | `s1_recovery_history` | BEFORE UPDATE OR DELETE |

SQL-owned constraints/guards are deliberately retained inside the immutable migration. EF cannot use a nullable property in an alternate key without making it required; the quarantine registry therefore uses a unique index and migration-owned composite foreign keys. The reverse user→mapping FK is deferred to transaction commit so credentials and mapping can be inserted atomically. PostgreSQL `btree_gist` supplies concurrency-safe exclusion checks.

## Alternate unique keys

- `AK_ProcessingContextVersions_ContextId_Version_OrganizationId`
- `AK_RecoveryRequests_RequestId_TargetUserId`
- `AK_AccessChallenges_ChallengeId_UserId_Purpose`
