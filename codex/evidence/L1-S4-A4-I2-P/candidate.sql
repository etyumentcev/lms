ALTER TABLE public."ProcessingRestrictions"
  ADD CONSTRAINT "AK_Restriction_IdOrg" UNIQUE ("RestrictionId", "OrganizationId");

CREATE TABLE public."Prs1Decisions" (
  "DecisionId" uuid PRIMARY KEY,
  "OrganizationId" uuid NOT NULL REFERENCES public."Organizations"("OrganizationId") ON DELETE RESTRICT,
  "AttestedByActorId" uuid NOT NULL REFERENCES public."Actors"("ActorId") ON DELETE RESTRICT,
  "DecisionRef" text COLLATE "C" NOT NULL CHECK (length("DecisionRef") > 0),
  "ApprovedCommand" jsonb NOT NULL CHECK (jsonb_typeof("ApprovedCommand") = 'object'),
  "CommandDigest" bytea NOT NULL CHECK (octet_length("CommandDigest") = 32),
  "Evidence" jsonb NOT NULL CHECK (jsonb_typeof("Evidence") = 'object' AND "Evidence" <> '{}'::jsonb),
  "RecordedAt" timestamptz NOT NULL DEFAULT clock_timestamp(),
  UNIQUE ("DecisionId", "OrganizationId")
);

CREATE TABLE public."Prs1Commands" (
  "CommandId" uuid PRIMARY KEY,
  "OrganizationId" uuid NOT NULL,
  "DecisionId" uuid NOT NULL,
  "Kind" text COLLATE "C" NOT NULL CHECK ("Kind" IN ('Issue','Reissue','Withdraw')),
  "NewRestrictionId" uuid,
  "OldRestrictionId" uuid,
  "ExpectedOldVersion" bigint,
  "CommandDigest" bytea NOT NULL CHECK (octet_length("CommandDigest") = 32),
  "RecordedAt" timestamptz NOT NULL DEFAULT clock_timestamp(),
  UNIQUE ("CommandId", "OrganizationId"),
  UNIQUE ("NewRestrictionId"),
  FOREIGN KEY ("DecisionId", "OrganizationId") REFERENCES public."Prs1Decisions"("DecisionId", "OrganizationId") ON DELETE RESTRICT,
  FOREIGN KEY ("OldRestrictionId", "OrganizationId") REFERENCES public."ProcessingRestrictions"("RestrictionId", "OrganizationId") ON DELETE RESTRICT,
  CHECK (("Kind" = 'Issue' AND "NewRestrictionId" IS NOT NULL AND "OldRestrictionId" IS NULL AND "ExpectedOldVersion" IS NULL)
      OR ("Kind" = 'Reissue' AND "NewRestrictionId" IS NOT NULL AND "OldRestrictionId" IS NOT NULL AND "ExpectedOldVersion" IS NOT NULL AND "ExpectedOldVersion" > 0 AND "NewRestrictionId" <> "OldRestrictionId")
      OR ("Kind" = 'Withdraw' AND "NewRestrictionId" IS NULL AND "OldRestrictionId" IS NOT NULL AND "ExpectedOldVersion" IS NOT NULL AND "ExpectedOldVersion" > 0))
);

CREATE TABLE public."RestrictionBirths" (
  "RestrictionId" uuid PRIMARY KEY,
  "OrganizationId" uuid NOT NULL,
  "BirthTransaction" xid8 NOT NULL,
  FOREIGN KEY ("RestrictionId", "OrganizationId") REFERENCES public."ProcessingRestrictions"("RestrictionId", "OrganizationId") ON DELETE RESTRICT
);

CREATE TABLE public."RestrictionSelectorProvenance" (
  "RestrictionId" uuid PRIMARY KEY,
  "OrganizationId" uuid NOT NULL,
  "ContextId" uuid NOT NULL,
  "OriginVersion" bigint NOT NULL CHECK ("OriginVersion" > 0),
  "SubjectActorId" uuid,
  "ProfileId" text COLLATE "C" NOT NULL CHECK ("ProfileId" = 'PRS1'),
  "ContractRevision" text COLLATE "C" NOT NULL CHECK ("ContractRevision" = 'R1-2026-10-10'),
  "CanonicalResourceRef" text COLLATE "C" NOT NULL CHECK (octet_length("CanonicalResourceRef") BETWEEN 1 AND 4096),
  "Operations" integer NOT NULL CHECK ("Operations" BETWEEN 1 AND 7),
  "CommandId" uuid NOT NULL UNIQUE,
  "DecisionId" uuid NOT NULL,
  "RecordedAt" timestamptz NOT NULL DEFAULT clock_timestamp(),
  "SupersedesRestrictionId" uuid,
  UNIQUE ("RestrictionId", "OrganizationId"),
  FOREIGN KEY ("RestrictionId", "OrganizationId") REFERENCES public."ProcessingRestrictions"("RestrictionId", "OrganizationId") ON DELETE RESTRICT,
  FOREIGN KEY ("ContextId", "OriginVersion", "OrganizationId") REFERENCES public."ProcessingContextVersions"("ContextId", "Version", "OrganizationId") ON DELETE RESTRICT,
  FOREIGN KEY ("OrganizationId", "SubjectActorId") REFERENCES public."OrganizationMemberships"("OrganizationId", "ActorId") ON DELETE RESTRICT,
  FOREIGN KEY ("CommandId", "OrganizationId") REFERENCES public."Prs1Commands"("CommandId", "OrganizationId") ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED,
  FOREIGN KEY ("DecisionId", "OrganizationId") REFERENCES public."Prs1Decisions"("DecisionId", "OrganizationId") ON DELETE RESTRICT,
  FOREIGN KEY ("SupersedesRestrictionId", "OrganizationId") REFERENCES public."ProcessingRestrictions"("RestrictionId", "OrganizationId") ON DELETE RESTRICT,
  CHECK ("SupersedesRestrictionId" IS NULL OR "SupersedesRestrictionId" <> "RestrictionId")
);
ALTER TABLE public."Prs1Commands" ADD CONSTRAINT "FK_Command_NewProvenance"
  FOREIGN KEY ("NewRestrictionId", "OrganizationId")
  REFERENCES public."RestrictionSelectorProvenance"("RestrictionId", "OrganizationId")
  ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;

CREATE FUNCTION public.prs1_record_birth() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
BEGIN
  INSERT INTO public."RestrictionBirths" VALUES
    (NEW."RestrictionId", NEW."OrganizationId", pg_current_xact_id());
  RETURN NEW;
END $$;
CREATE TRIGGER prs1_record_birth AFTER INSERT ON public."ProcessingRestrictions"
  FOR EACH ROW EXECUTE FUNCTION public.prs1_record_birth();

CREATE FUNCTION public.prs1_check_snapshot() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE r public."ProcessingRestrictions"%ROWTYPE;
BEGIN
  SELECT * INTO STRICT r FROM public."ProcessingRestrictions"
    WHERE "RestrictionId" = NEW."RestrictionId";
  IF NOT EXISTS (SELECT 1 FROM public."RestrictionBirths" b
      WHERE b."RestrictionId" = NEW."RestrictionId"
        AND b."OrganizationId" = NEW."OrganizationId"
        AND b."BirthTransaction" = pg_current_xact_id()) THEN
    RAISE EXCEPTION 'prs1_late_attestation' USING ERRCODE = '23514';
  END IF;
  IF ROW(r."OrganizationId",r."ContextId",r."OriginVersion",r."SubjectActorId",r."ResourceRef" COLLATE "C",r."Operations")
     IS DISTINCT FROM ROW(NEW."OrganizationId",NEW."ContextId",NEW."OriginVersion",NEW."SubjectActorId",NEW."CanonicalResourceRef" COLLATE "C",NEW."Operations")
     OR r."State" <> 'Active' OR r."Version" <> 1 THEN
    RAISE EXCEPTION 'prs1_snapshot_mismatch' USING ERRCODE = '23514';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM public."Prs1Commands" c
      WHERE c."CommandId"=NEW."CommandId" AND c."OrganizationId"=NEW."OrganizationId"
        AND c."NewRestrictionId"=NEW."RestrictionId" AND c."DecisionId"=NEW."DecisionId"
        AND c."Kind" IN ('Issue','Reissue')
        AND c."OldRestrictionId" IS NOT DISTINCT FROM NEW."SupersedesRestrictionId") THEN
    RAISE EXCEPTION 'prs1_command_pair_mismatch' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER prs1_check_snapshot BEFORE INSERT ON public."RestrictionSelectorProvenance"
  FOR EACH ROW EXECUTE FUNCTION public.prs1_check_snapshot();

CREATE FUNCTION public.prs1_command_complete() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE p public."RestrictionSelectorProvenance"%ROWTYPE;
BEGIN
  IF NOT EXISTS (SELECT 1 FROM public."Prs1Decisions" d
      WHERE d."DecisionId"=NEW."DecisionId" AND d."OrganizationId"=NEW."OrganizationId"
        AND d."CommandDigest"=NEW."CommandDigest") THEN
    RAISE EXCEPTION 'prs1_decision_mismatch' USING ERRCODE='23514';
  END IF;
  IF NEW."NewRestrictionId" IS NOT NULL THEN
    SELECT * INTO STRICT p FROM public."RestrictionSelectorProvenance"
      WHERE "RestrictionId"=NEW."NewRestrictionId";
    IF p."CommandId" <> NEW."CommandId" OR p."OrganizationId" <> NEW."OrganizationId"
       OR p."DecisionId" <> NEW."DecisionId"
       OR p."SupersedesRestrictionId" IS DISTINCT FROM NEW."OldRestrictionId"
       OR NOT EXISTS (SELECT 1 FROM public."ProcessingRestrictions" r
         WHERE r."RestrictionId"=p."RestrictionId" AND r."State"='Active' AND r."Version"=1) THEN
      RAISE EXCEPTION 'prs1_pair_incomplete' USING ERRCODE='23514';
    END IF;
  END IF;
  IF NEW."OldRestrictionId" IS NOT NULL AND NOT EXISTS (
      SELECT 1 FROM public."ProcessingRestrictions" r WHERE r."RestrictionId"=NEW."OldRestrictionId"
        AND r."State"='Withdrawn' AND r."Version"=NEW."ExpectedOldVersion"+1) THEN
    RAISE EXCEPTION 'prs1_withdraw_incomplete' USING ERRCODE='23514';
  END IF;
  RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER prs1_command_complete AFTER INSERT ON public."Prs1Commands"
  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.prs1_command_complete();

CREATE FUNCTION public.prs1_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'prs1_history_immutable' USING ERRCODE='23514'; END $$;
CREATE TRIGGER prs1_provenance_immutable BEFORE UPDATE OR DELETE ON public."RestrictionSelectorProvenance"
  FOR EACH ROW EXECUTE FUNCTION public.prs1_immutable();
CREATE TRIGGER prs1_decision_immutable BEFORE UPDATE OR DELETE ON public."Prs1Decisions"
  FOR EACH ROW EXECUTE FUNCTION public.prs1_immutable();
CREATE TRIGGER prs1_command_immutable BEFORE UPDATE OR DELETE ON public."Prs1Commands"
  FOR EACH ROW EXECUTE FUNCTION public.prs1_immutable();
CREATE TRIGGER prs1_birth_immutable BEFORE UPDATE OR DELETE ON public."RestrictionBirths"
  FOR EACH ROW EXECUTE FUNCTION public.prs1_immutable();


-- Educational mutation outcome ledger; not administrative selector provenance.
CREATE TABLE public."Prs1MutationReceipts" (
  "OrganizationId" uuid NOT NULL REFERENCES public."Organizations"("OrganizationId") ON DELETE RESTRICT,
  "CommandId" uuid NOT NULL,
  "ActorId" uuid NOT NULL REFERENCES public."Actors"("ActorId") ON DELETE RESTRICT,
  "Kind" text COLLATE "C" NOT NULL CHECK ("Kind" IN ('Submit','Review')),
  "CommandDigest" bytea NOT NULL CHECK (octet_length("CommandDigest")=32),
  "WorkId" uuid NOT NULL REFERENCES public."ProblemWorks"("Id") ON DELETE RESTRICT,
  "SubmissionId" uuid NOT NULL,
  "ReviewId" uuid,
  "ExpectedWorkVersion" bigint CHECK ("ExpectedWorkVersion" >= 0),
  "ResultWorkVersion" bigint NOT NULL CHECK ("ResultWorkVersion" > 0),
  "RecordedAt" timestamptz NOT NULL DEFAULT clock_timestamp(),
  PRIMARY KEY ("OrganizationId", "CommandId"),
  FOREIGN KEY ("WorkId", "SubmissionId") REFERENCES public."Submissions"("ProblemWorkId", "Id") ON DELETE RESTRICT,
  FOREIGN KEY ("ReviewId") REFERENCES public."Reviews"("Id") ON DELETE RESTRICT,
  CHECK (("Kind"='Submit' AND "ReviewId" IS NULL)
      OR ("Kind"='Review' AND "ReviewId" IS NOT NULL AND "ExpectedWorkVersion" IS NOT NULL))
);
CREATE TRIGGER prs1_mutation_receipt_immutable BEFORE UPDATE OR DELETE ON public."Prs1MutationReceipts"
  FOR EACH ROW EXECUTE FUNCTION public.prs1_immutable();
REVOKE ALL ON public."Prs1MutationReceipts" FROM PUBLIC;

REVOKE ALL ON public."RestrictionSelectorProvenance", public."Prs1Decisions",
  public."Prs1Commands", public."RestrictionBirths" FROM PUBLIC;
REVOKE ALL ON FUNCTION public.prs1_record_birth(), public.prs1_check_snapshot(),
  public.prs1_command_complete(), public.prs1_immutable() FROM PUBLIC;