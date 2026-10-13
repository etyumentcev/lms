CREATE ROLE prs1_owner NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
CREATE ROLE prs1_writer NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
CREATE ROLE prs1_reader NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
CREATE ROLE prs1_app NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE,CREATE ON SCHEMA public TO prs1_owner;
SET ROLE prs1_owner;
CREATE TABLE "Organizations" ("OrganizationId" uuid PRIMARY KEY);
CREATE TABLE "Actors" ("ActorId" uuid PRIMARY KEY);
CREATE TABLE "OrganizationMemberships" ("OrganizationId" uuid,"ActorId" uuid,PRIMARY KEY("OrganizationId","ActorId"));
CREATE TABLE "ProcessingContextVersions" ("ContextId" uuid,"Version" bigint,"OrganizationId" uuid,PRIMARY KEY("ContextId","Version","OrganizationId"));
CREATE TABLE "ProblemWorks" ("Id" uuid PRIMARY KEY);
CREATE TABLE "Submissions" ("Id" uuid PRIMARY KEY,"ProblemWorkId" uuid, UNIQUE("ProblemWorkId","Id"));
CREATE TABLE "Reviews" ("Id" uuid PRIMARY KEY);
CREATE TABLE "ProcessingRestrictions" ("RestrictionId" uuid PRIMARY KEY,"ContextId" uuid NOT NULL,"OriginVersion" bigint NOT NULL,"OrganizationId" uuid NOT NULL,"SubjectActorId" uuid,"ResourceRef" text,"Operations" integer NOT NULL,"State" text NOT NULL,"Version" bigint NOT NULL,
CHECK ("State" IN ('Active','Withdrawn') AND "Version">0 AND "Operations">0 AND "Operations"<=31),
FOREIGN KEY("ContextId","OriginVersion","OrganizationId") REFERENCES "ProcessingContextVersions"("ContextId","Version","OrganizationId") ON DELETE RESTRICT,
FOREIGN KEY("OrganizationId","SubjectActorId") REFERENCES "OrganizationMemberships"("OrganizationId","ActorId") ON DELETE RESTRICT);
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
                END $$;CREATE TRIGGER s1_restriction_provenance BEFORE UPDATE OR DELETE ON "ProcessingRestrictions" FOR EACH ROW EXECUTE FUNCTION s1_stable_key('RestrictionId','ContextId','OriginVersion','OrganizationId','SubjectActorId','ResourceRef','Operations');INSERT INTO "Organizations" VALUES ('00000000-0000-0000-0000-000000000001'); INSERT INTO "Actors" VALUES ('00000000-0000-0000-0000-000000000002'); INSERT INTO "ProcessingContextVersions" VALUES ('00000000-0000-0000-0000-000000000003',1,'00000000-0000-0000-0000-000000000001');INSERT INTO "ProcessingRestrictions" VALUES ('00000000-0000-0000-0000-000000000010','00000000-0000-0000-0000-000000000003',1,'00000000-0000-0000-0000-000000000001',NULL,'canonical',1,'Active',1);