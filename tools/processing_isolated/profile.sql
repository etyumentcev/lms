-- C2-H disposable database ONLY. Fixed roles created by the isolated test launcher.
-- No credentials here. Owner applies after the complete EF migration chain.
-- Candidate BLOCKED for S2: see executable audit-writer conflict test.
BEGIN;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO c2h_runtime,c2h_processing;
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM PUBLIC,c2h_runtime,c2h_processing;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM PUBLIC,c2h_runtime,c2h_processing;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public FROM PUBLIC,c2h_runtime,c2h_processing;
ALTER DEFAULT PRIVILEGES FOR ROLE c2h_owner REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
ALTER DEFAULT PRIVILEGES FOR ROLE c2h_owner REVOKE ALL ON TABLES FROM PUBLIC;
ALTER DEFAULT PRIVILEGES FOR ROLE c2h_owner REVOKE ALL ON SEQUENCES FROM PUBLIC;
GRANT SELECT ON "AccessEnrollments","AccessLessons","AccessTeachers","AccessAttachments",
 "StoredAttachments","PublishedContentVersions","ProblemAssignments","ProblemWorks",
 "Submissions","Messages","Reviews","DomainEvents",
 "Organizations","OrganizationProfiles","OrganizationParticipantProfiles","Actors","ActorIdentities","AccountSecurityState","AspNetUsers",
 "IdentitySessions","RefreshVerifiers","OrganizationMemberships","EnrollmentScopes",
 "TeacherAssignmentScopes","CourseEditorScopes","CourseRegistry","CourseGroups","AssignmentScopes","AuthorityHistory","AttachmentScopes",
 "ProcessingContextVersions","CourseContextBindings","ProcessingRestrictions"
 TO c2h_runtime;
GRANT INSERT ON "ProblemWorks","Submissions","Messages","Reviews","DomainEvents",
 "AccessAttachments","StoredAttachments","IdentitySessions","RefreshVerifiers" TO c2h_runtime;
GRANT UPDATE ("Status","Version","AcceptedAt") ON "ProblemWorks" TO c2h_runtime;
GRANT UPDATE ("IsEligible") ON "AccessAttachments" TO c2h_runtime;
GRANT UPDATE ("SubmissionId","MessageId","Deleted") ON "StoredAttachments" TO c2h_runtime;
GRANT UPDATE ON "IdentitySessions","RefreshVerifiers" TO c2h_runtime;
-- S2 SELECT FOR UPDATE requires a column UPDATE privilege even with no mutation.
GRANT UPDATE ("UserId") ON "AccountSecurityState","ActorIdentities" TO c2h_runtime;
GRANT UPDATE ("PasswordHash") ON "AspNetUsers" TO c2h_runtime;
GRANT EXECUTE ON FUNCTION public.s3_change_authority(text,text,uuid,uuid,bigint,bigint,text,text,uuid,text,integer) TO c2h_runtime;
GRANT EXECUTE ON FUNCTION public.processing_change(uuid,uuid,bigint,text,jsonb),
 public.processing_find_command(uuid,uuid,bigint,text,jsonb) TO c2h_processing;
SELECT public.processing_validate_operational_role('c2h_runtime');
SELECT public.processing_validate_operational_role('c2h_processing');
COMMIT;
