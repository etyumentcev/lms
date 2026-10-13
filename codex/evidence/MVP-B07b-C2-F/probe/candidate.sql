-- EXPERIMENT ONLY. Never apply to an application database.
CREATE SCHEMA c2f;
REVOKE ALL ON SCHEMA c2f FROM PUBLIC;
CREATE TABLE c2f."ProcessingGuard" (
 org uuid PRIMARY KEY REFERENCES public."Organizations"("OrganizationId") ON DELETE RESTRICT,
 revision bigint NOT NULL CHECK(revision > 0), compatible boolean NOT NULL DEFAULT false);
CREATE TABLE c2f."ImportAttempt" (
 id uuid PRIMARY KEY, org uuid NOT NULL, course text NOT NULL, owner uuid NOT NULL REFERENCES public."Actors"("ActorId") ON DELETE RESTRICT,
 context uuid NOT NULL, context_version bigint NOT NULL, binding_from timestamptz NOT NULL,
 admitted timestamptz NOT NULL DEFAULT clock_timestamp(), expires timestamptz NOT NULL,
 command_id uuid NOT NULL, admission_fingerprint text NOT NULL,
 state text NOT NULL CHECK(state IN ('Receiving','Validating','Ready','Invalid','Failed','Cancelled','Expired')),
 version bigint NOT NULL CHECK(version>0), epoch bigint NOT NULL DEFAULT 1,
 operation_id uuid NOT NULL, fingerprint text NOT NULL,
 source jsonb, result jsonb, generation uuid,
 cleanup text NOT NULL DEFAULT 'None' CHECK(cleanup IN ('None','Pending','Deleting','Deleted','Quarantined')),
 CHECK(expires=admitted+interval '24 hours'),
 CHECK((result IS NULL AND generation IS NULL) OR (result IS NOT NULL AND generation IS NOT NULL)),
 CHECK(state <> 'Ready' OR (source IS NOT NULL AND result IS NOT NULL AND cleanup='None')),
 UNIQUE(org,owner,command_id),
 FOREIGN KEY(course,org) REFERENCES public."CourseRegistry"("CourseId","OrganizationId") ON DELETE RESTRICT,
 FOREIGN KEY(context,context_version,org) REFERENCES public."ProcessingContextVersions"("ContextId","Version","OrganizationId") ON DELETE RESTRICT,
 FOREIGN KEY(course,binding_from) REFERENCES public."CourseContextBindings"("CourseId","EffectiveFrom") ON DELETE RESTRICT);
CREATE TABLE c2f."ImportTransitions" (
 import_id uuid NOT NULL REFERENCES c2f."ImportAttempt"(id) ON DELETE RESTRICT,
 operation_id uuid NOT NULL, fingerprint text NOT NULL, from_version bigint NOT NULL, to_version bigint NOT NULL,
 outcome text NOT NULL, audit_id uuid NOT NULL UNIQUE REFERENCES public."SecurityAudit"("AuditId") ON DELETE RESTRICT,
 PRIMARY KEY(import_id,operation_id), UNIQUE(import_id,to_version));
CREATE TRIGGER retained_journal BEFORE UPDATE OR DELETE ON c2f."ImportTransitions" FOR EACH ROW EXECUTE FUNCTION public.s1_immutable_row();
CREATE FUNCTION c2f.check_row() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'retained tombstone' USING ERRCODE='23514'; END IF;
 IF TG_OP='INSERT' THEN
  IF NEW.state<>'Receiving' OR NEW.version<>1 OR NEW.source IS NOT NULL OR NEW.result IS NOT NULL THEN RAISE EXCEPTION 'admission shape' USING ERRCODE='23514'; END IF;
 ELSE
  IF (NEW.id,NEW.org,NEW.course,NEW.owner,NEW.context,NEW.context_version,NEW.binding_from,NEW.admitted,NEW.expires,NEW.command_id,NEW.admission_fingerprint)
    IS DISTINCT FROM (OLD.id,OLD.org,OLD.course,OLD.owner,OLD.context,OLD.context_version,OLD.binding_from,OLD.admitted,OLD.expires,OLD.command_id,OLD.admission_fingerprint)
   THEN RAISE EXCEPTION 'immutable identity/TTL' USING ERRCODE='23514'; END IF;
  IF NEW.version<>OLD.version+1 THEN RAISE EXCEPTION 'version' USING ERRCODE='23514'; END IF;
  IF NOT ((OLD.state='Receiving' AND NEW.state IN ('Validating','Invalid','Failed','Cancelled','Expired')) OR
          (OLD.state='Validating' AND NEW.state IN ('Ready','Invalid','Failed','Cancelled','Expired')) OR
          (OLD.state='Ready' AND NEW.state IN ('Failed','Expired')) OR
          (OLD.state IN ('Invalid','Failed','Cancelled') AND NEW.state='Expired')) THEN RAISE EXCEPTION 'transition' USING ERRCODE='23514'; END IF;
  IF OLD.source IS NOT NULL AND NEW.source IS DISTINCT FROM OLD.source THEN RAISE EXCEPTION 'source immutable' USING ERRCODE='23514'; END IF;
  IF OLD.result IS NOT NULL AND (NEW.result,NEW.generation) IS DISTINCT FROM (OLD.result,OLD.generation) THEN RAISE EXCEPTION 'generation immutable' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER import_check BEFORE INSERT OR UPDATE OR DELETE ON c2f."ImportAttempt" FOR EACH ROW EXECUTE FUNCTION c2f.check_row();
CREATE FUNCTION c2f.audit_row() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
DECLARE a uuid:=gen_random_uuid();
BEGIN
 INSERT INTO public."SecurityAudit"("AuditId","OrganizationId","ActorId","Action","SubjectRef","ResourceRef","Result","AuthorityVersion","ContextId","ContextVersion","TraceId","EvidenceRef","OccurredAt")
 VALUES(a,NEW.org,NEW.owner,'C2F.synthetic',NEW.id::text,NEW.course,NEW.state,NEW.version,NEW.context,NEW.context_version,NEW.operation_id::text,'controlled-fixture',clock_timestamp());
 INSERT INTO c2f."ImportTransitions" VALUES(NEW.id,NEW.operation_id,NEW.fingerprint,CASE WHEN TG_OP='INSERT' THEN 0 ELSE OLD.version END,NEW.version,NEW.state,a);
 RETURN NEW;
END $$;
CREATE TRIGGER import_audit AFTER INSERT OR UPDATE ON c2f."ImportAttempt" FOR EACH ROW EXECUTE FUNCTION c2f.audit_row();
CREATE FUNCTION c2f.lock_org(o uuid) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
BEGIN
 PERFORM 1 FROM public."Organizations" WHERE "OrganizationId"=o AND "State"='Enabled' AND "SyntheticOnly" FOR UPDATE;
 IF NOT FOUND THEN RAISE EXCEPTION 'organization denied' USING ERRCODE='P0001'; END IF;
 UPDATE c2f."ProcessingGuard" SET revision=revision+1 WHERE org=o;
 IF NOT FOUND THEN RAISE EXCEPTION 'guard missing' USING ERRCODE='P0001'; END IF;
END $$;
CREATE FUNCTION c2f.authorized(o uuid,c text,a uuid) RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
 SELECT EXISTS(SELECT 1 FROM public."OrganizationMemberships" m JOIN public."TeacherAssignmentScopes" t
 ON t."OrganizationId"=m."OrganizationId" AND t."TeacherId"=m."ActorId" AND t."MembershipGeneration"=m."Generation"
 JOIN public."CourseRegistry" r ON r."CourseId"=t."CourseId" AND r."OrganizationId"=t."OrganizationId"
 WHERE m."OrganizationId"=o AND m."ActorId"=a AND m."State"='Active' AND m."EducationalRole"='Teacher'
 AND t."State"='Active' AND t."GroupId" IS NULL AND t."CourseId"=c AND r."MappingState"='Verified')
$$;
CREATE FUNCTION c2f.context_ok(o uuid,c text,x uuid,v bigint,b timestamptz) RETURNS boolean LANGUAGE sql VOLATILE SET search_path=pg_catalog AS $$
 SELECT count(*)=1 AND coalesce(bool_and(k."ContextId"=x AND k."ContextVersion"=v AND k."EffectiveFrom"=b AND p."SyntheticOnly" AND p."ApprovalState"='SyntheticOnly' AND p."EffectiveFrom"<=clock_timestamp()),false)
 FROM public."CourseContextBindings" k JOIN public."ProcessingContextVersions" p ON (p."ContextId",p."Version",p."OrganizationId")=(k."ContextId",k."ContextVersion",k."OrganizationId")
 WHERE k."OrganizationId"=o AND k."CourseId"=c AND k."EffectiveFrom"<=clock_timestamp() AND (k."EffectiveTo" IS NULL OR k."EffectiveTo">clock_timestamp())
$$;
-- All restrictions in context deny in this deliberately over-conservative fixed fixture probe.
CREATE FUNCTION c2f.unrestricted(o uuid,x uuid) RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
 SELECT NOT EXISTS(SELECT 1 FROM public."ProcessingRestrictions" WHERE "OrganizationId"=o AND "ContextId"=x AND "State"='Active')
$$;
CREATE FUNCTION c2f.admit(i uuid,o uuid,c text,a uuid,cmd uuid,fp text) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
DECLARE b public."CourseContextBindings"; old c2f."ImportAttempt"; t timestamptz;
BEGIN
 PERFORM c2f.lock_org(o);
 IF NOT c2f.authorized(o,c,a) THEN RAISE EXCEPTION 'authority denied' USING ERRCODE='P0001'; END IF;
 SELECT * INTO old FROM c2f."ImportAttempt" WHERE org=o AND owner=a AND command_id=cmd;
 IF FOUND THEN
  IF old.admission_fingerprint<>fp OR old.course<>c THEN RAISE EXCEPTION 'fingerprint conflict' USING ERRCODE='23505'; END IF;
  IF old.expires<=clock_timestamp() OR NOT c2f.context_ok(o,c,old.context,old.context_version,old.binding_from) OR NOT c2f.unrestricted(o,old.context) THEN RAISE EXCEPTION 'replay denied' USING ERRCODE='P0001'; END IF;
  RETURN old.id;
 END IF;
 SELECT * INTO b FROM public."CourseContextBindings" WHERE "OrganizationId"=o AND "CourseId"=c AND "EffectiveFrom"<=clock_timestamp() AND ("EffectiveTo" IS NULL OR "EffectiveTo">clock_timestamp());
 IF b."ContextId" IS NULL OR NOT c2f.context_ok(o,c,b."ContextId",b."ContextVersion",b."EffectiveFrom") OR NOT c2f.unrestricted(o,b."ContextId") THEN RAISE EXCEPTION 'context denied' USING ERRCODE='P0001'; END IF;
 t:=clock_timestamp();
 INSERT INTO c2f."ImportAttempt"(id,org,course,owner,context,context_version,binding_from,admitted,expires,command_id,admission_fingerprint,state,version,operation_id,fingerprint)
 VALUES(i,o,c,a,b."ContextId",b."ContextVersion",b."EffectiveFrom",t,t+interval '24 hours',cmd,fp,'Receiving',1,cmd,fp);
 RETURN i;
END $$;
CREATE FUNCTION c2f.descriptor_ok(d jsonb,parts text[]) RETURNS boolean LANGUAGE plpgsql IMMUTABLE SET search_path=pg_catalog AS $$
DECLARE k text;
BEGIN
 IF d IS NULL OR jsonb_typeof(d)<>'object' OR (SELECT count(*) FROM jsonb_object_keys(d))<>cardinality(parts) THEN RETURN false; END IF;
 FOREACH k IN ARRAY parts LOOP
  IF NOT coalesce(jsonb_typeof(d->k)='object' AND d->k->>'key' ~ '^[a-z]+$' AND d->k->>'sha256' ~ '^[0-9a-f]{64}$' AND d->k->>'length' ~ '^[0-9]+$' AND d->k->>'generation' ~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$',false) THEN RETURN false; END IF;
 END LOOP;
 RETURN true;
END $$;
CREATE FUNCTION c2f.step(i uuid,o uuid,a uuid,ev bigint,op uuid,fp text,target text,sealed boolean DEFAULT false,descriptor jsonb DEFAULT NULL,g uuid DEFAULT NULL)
 RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
DECLARE r c2f."ImportAttempt"; j c2f."ImportTransitions";
BEGIN
 PERFORM c2f.lock_org(o);
 SELECT * INTO r FROM c2f."ImportAttempt" WHERE id=i AND org=o AND owner=a FOR UPDATE;
 IF NOT FOUND OR NOT c2f.authorized(o,r.course,a) THEN RAISE EXCEPTION 'owner/authority denied' USING ERRCODE='P0001'; END IF;
 IF target<>'Expired' AND (r.expires<=clock_timestamp() OR NOT c2f.context_ok(o,r.course,r.context,r.context_version,r.binding_from) OR NOT c2f.unrestricted(o,r.context)) THEN RAISE EXCEPTION 'current gate denied' USING ERRCODE='P0001'; END IF;
 SELECT * INTO j FROM c2f."ImportTransitions" WHERE import_id=i AND operation_id=op;
 IF FOUND THEN
  IF j.fingerprint<>fp OR j.outcome<>target THEN RAISE EXCEPTION 'operation fingerprint conflict' USING ERRCODE='23505'; END IF;
  RETURN j.to_version;
 END IF;
 IF r.version<>ev THEN RAISE EXCEPTION 'stale CAS' USING ERRCODE='P0001'; END IF;
 IF target='Expired' AND r.expires>clock_timestamp() THEN RAISE EXCEPTION 'not expired' USING ERRCODE='P0001'; END IF;
 IF target='Ready' AND (NOT (SELECT compatible FROM c2f."ProcessingGuard" WHERE org=o) OR NOT sealed OR NOT c2f.descriptor_ok(descriptor,ARRAY['student','teacher','receipt']) OR g IS NULL OR EXISTS(SELECT 1 FROM jsonb_each(descriptor) d WHERE d.value->>'generation' IS DISTINCT FROM g::text)) THEN RAISE EXCEPTION 'Ready blocked' USING ERRCODE='P0001'; END IF;
 IF target='Validating' AND (NOT sealed OR NOT c2f.descriptor_ok(descriptor,ARRAY['source'])) THEN RAISE EXCEPTION 'source not sealed' USING ERRCODE='P0001'; END IF;
 UPDATE c2f."ImportAttempt" SET state=target,version=version+1,operation_id=op,fingerprint=fp,
 epoch=CASE WHEN target='Cancelled' THEN epoch+1 ELSE epoch END,
 source=CASE WHEN target='Validating' THEN descriptor ELSE source END,
 result=CASE WHEN target='Ready' THEN descriptor ELSE result END,
 generation=CASE WHEN target='Ready' THEN g ELSE generation END WHERE id=i;
 RETURN ev+1;
END $$;
-- Synthetic read predicate only: no content, HTTP, paths or delivery operation.
CREATE FUNCTION c2f.readable(i uuid,o uuid,a uuid) RETURNS boolean LANGUAGE sql VOLATILE SECURITY DEFINER SET search_path=pg_catalog AS $$
 SELECT EXISTS(SELECT 1 FROM c2f."ImportAttempt" r JOIN public."Organizations" z ON z."OrganizationId"=r.org
 WHERE r.id=i AND r.org=o AND r.owner=a AND r.state='Ready' AND r.cleanup='None' AND r.expires>clock_timestamp()
 AND z."State"='Enabled' AND z."SyntheticOnly" AND c2f.authorized(o,r.course,a)
 AND c2f.context_ok(o,r.course,r.context,r.context_version,r.binding_from) AND c2f.unrestricted(o,r.context))
$$;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA c2f FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA c2f FROM PUBLIC;
CREATE ROLE c2f_serving NOLOGIN;
CREATE ROLE c2f_executor NOLOGIN;
GRANT USAGE ON SCHEMA c2f TO c2f_serving,c2f_executor;
GRANT EXECUTE ON FUNCTION c2f.admit(uuid,uuid,text,uuid,uuid,text),c2f.step(uuid,uuid,uuid,bigint,uuid,text,text,boolean,jsonb,uuid),c2f.readable(uuid,uuid,uuid) TO c2f_executor;
