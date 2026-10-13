-- Exact raw SQL body from 20261011120000_ProcessingWriterGuardC2G; candidate only.
LOCK TABLE public."Organizations" IN SHARE ROW EXCLUSIVE MODE;
CREATE TABLE public."ProcessingGuards" (
  "OrganizationId" uuid PRIMARY KEY REFERENCES public."Organizations"("OrganizationId") ON DELETE RESTRICT,
  "Revision" bigint NOT NULL CHECK ("Revision" >= 0));
CREATE TABLE public."ProcessingWriteJournal" (
  "OrganizationId" uuid NOT NULL REFERENCES public."Organizations"("OrganizationId") ON DELETE RESTRICT,
  "OperationId" uuid NOT NULL, "Fingerprint" text NOT NULL,
  "Revision" bigint NOT NULL CHECK ("Revision" > 0),
  "AuditId" uuid NOT NULL UNIQUE REFERENCES public."SecurityAudit"("AuditId") ON DELETE RESTRICT,
  PRIMARY KEY ("OrganizationId","OperationId"), UNIQUE ("OrganizationId","Revision"));
REVOKE ALL ON public."ProcessingGuards",public."ProcessingWriteJournal" FROM PUBLIC;
CREATE TRIGGER processing_journal_immutable BEFORE UPDATE OR DELETE ON public."ProcessingWriteJournal"
  FOR EACH ROW EXECUTE FUNCTION public.s1_immutable_row();

CREATE FUNCTION public.processing_initialize_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
BEGIN
  INSERT INTO public."ProcessingGuards" VALUES(NEW."OrganizationId",0);
  RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION public.processing_initialize_guard() FROM PUBLIC;
CREATE TRIGGER processing_organization_guard AFTER INSERT ON public."Organizations"
  FOR EACH ROW EXECUTE FUNCTION public.processing_initialize_guard();
INSERT INTO public."ProcessingGuards" SELECT "OrganizationId",0 FROM public."Organizations";

-- Both rows are locked before any affected-row DML. The helper grants no user authority.
-- SELECT FOR UPDATE also rejects a changed guard row in an old Serializable snapshot.
CREATE FUNCTION public.processing_lock(p_org uuid) RETURNS bigint
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
DECLARE v bigint;
BEGIN
  IF p_org IS NULL OR p_org='00000000-0000-0000-0000-000000000000' THEN RAISE EXCEPTION 'invalid_processing_org'; END IF;
  PERFORM 1 FROM public."Organizations" WHERE "OrganizationId"=p_org AND "SyntheticOnly" FOR UPDATE;
  IF NOT FOUND THEN RAISE EXCEPTION 'processing_org_missing'; END IF;
  SELECT "Revision" INTO v FROM public."ProcessingGuards" WHERE "OrganizationId"=p_org FOR UPDATE;
  IF NOT FOUND THEN RAISE EXCEPTION 'processing_guard_missing'; END IF;
  RETURN v;
END $$;
REVOKE ALL ON FUNCTION public.processing_lock(uuid) FROM PUBLIC;

CREATE FUNCTION public.processing_begin_write(p_org uuid,p_expected bigint) RETURNS bigint
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
DECLARE v bigint;
BEGIN
  v:=public.processing_lock(p_org);
  IF p_expected IS NULL OR p_expected<0 OR v<>p_expected THEN RAISE EXCEPTION 'processing_revision_conflict'; END IF;
  UPDATE public."ProcessingGuards" SET "Revision"="Revision"+1 WHERE "OrganizationId"=p_org RETURNING "Revision" INTO v;
  RETURN v;
END $$;
REVOKE ALL ON FUNCTION public.processing_begin_write(uuid,bigint) FROM PUBLIC;

-- Transaction-owned conservative current decision, independent of ordinary JWT freshness.
-- The complete subject set and immutable context/binding must be server-resolved upstream.
CREATE FUNCTION public.processing_assert_current(p_org uuid,p_course text,p_context uuid,p_version bigint,
  p_binding timestamptz,p_subjects uuid[]) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
DECLARE n bigint; valid boolean; at_time timestamptz:=clock_timestamp();
BEGIN
  IF p_subjects IS NULL OR cardinality(p_subjects) NOT BETWEEN 1 AND 256 OR array_position(p_subjects,NULL) IS NOT NULL
     OR '00000000-0000-0000-0000-000000000000'::uuid=ANY(p_subjects) THEN RAISE EXCEPTION 'processing_subjects_missing'; END IF;
  IF NOT EXISTS(SELECT 1 FROM public."Organizations" WHERE "OrganizationId"=p_org AND "State"='Enabled' AND "SyntheticOnly")
    THEN RAISE EXCEPTION 'processing_org_denied'; END IF;
  SELECT count(*),coalesce(bool_and(b."ContextId"=p_context AND b."ContextVersion"=p_version AND b."EffectiveFrom"=p_binding
    AND c."SyntheticOnly" AND c."ApprovalState"='SyntheticOnly' AND c."EffectiveFrom"<=at_time),false)
  INTO n,valid FROM public."CourseContextBindings" b
  JOIN public."ProcessingContextVersions" c ON (c."ContextId",c."Version",c."OrganizationId")=(b."ContextId",b."ContextVersion",b."OrganizationId")
  JOIN public."CourseRegistry" r ON (r."CourseId",r."OrganizationId")=(b."CourseId",b."OrganizationId") AND r."MappingState"='Verified'
  WHERE b."OrganizationId"=p_org AND b."CourseId"=p_course AND b."EffectiveFrom"<=at_time
    AND (b."EffectiveTo" IS NULL OR b."EffectiveTo">at_time);
  IF n<>1 OR valid IS DISTINCT FROM true THEN RAISE EXCEPTION 'processing_context_denied'; END IF;
  IF EXISTS(SELECT 1 FROM public."ProcessingRestrictions" WHERE "OrganizationId"=p_org AND "ContextId"=p_context AND "State"='Active'
    AND ("SubjectActorId" IS NULL OR "SubjectActorId"=ANY(p_subjects))) THEN RAISE EXCEPTION 'processing_restricted'; END IF;
END $$;
REVOKE ALL ON FUNCTION public.processing_assert_current(uuid,text,uuid,bigint,timestamptz,uuid[]) FROM PUBLIC;

-- public is needed by the existing s1_binding_effective trigger; explicit pg_temp last
-- prevents caller-owned temporary tables from shadowing that legacy unqualified reference.
CREATE FUNCTION public.processing_change(p_org uuid,p_operation uuid,p_expected bigint,p_action text,p_payload jsonb) RETURNS bigint
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE revision bigint; previous public."ProcessingWriteJournal"%ROWTYPE; fingerprint text; audit uuid:=gen_random_uuid();
  context_id uuid; context_version bigint; course text; at_time timestamptz; binding_from timestamptz; allowed text[];
BEGIN
  IF p_operation IS NULL OR p_operation='00000000-0000-0000-0000-000000000000' OR p_expected IS NULL OR p_expected<0
    OR p_payload IS NULL OR jsonb_typeof(p_payload)<>'object' OR octet_length(p_payload::text)>4096 THEN RAISE EXCEPTION 'invalid_processing_command'; END IF;
  allowed:=CASE p_action
    WHEN 'AddContext' THEN ARRAY['ContextId','Version','EffectiveFrom','ApprovalState','PurposeRef','RetentionPolicyRef']
    WHEN 'OpenBinding' THEN ARRAY['CourseId','ContextId','ContextVersion','EffectiveFrom']
    WHEN 'CloseBinding' THEN ARRAY['CourseId','ExpectedBindingFrom']
    WHEN 'ReplaceBinding' THEN ARRAY['CourseId','ExpectedBindingFrom','ContextId','ContextVersion']
    WHEN 'AddRestriction' THEN ARRAY['RestrictionId','ContextId','ContextVersion','SubjectActorId','Operations','ResourceRef']
    WHEN 'WithdrawRestriction' THEN ARRAY['RestrictionId','ExpectedVersion']
    WHEN 'SetOrganizationState' THEN ARRAY['State'] ELSE NULL END;
  IF allowed IS NULL OR EXISTS(SELECT 1 FROM jsonb_object_keys(p_payload) k WHERE NOT k=ANY(allowed))
    THEN RAISE EXCEPTION 'invalid_processing_action'; END IF;
  fingerprint:=encode(sha256(convert_to(jsonb_build_object('action',p_action,'expected',p_expected,'payload',p_payload)::text,'UTF8')),'hex');
  revision:=public.processing_lock(p_org);
  SELECT * INTO previous FROM public."ProcessingWriteJournal" WHERE "OrganizationId"=p_org AND "OperationId"=p_operation;
  IF FOUND THEN
    IF previous."Fingerprint"<>fingerprint THEN RAISE EXCEPTION 'processing_operation_conflict' USING ERRCODE='23505'; END IF;
    RETURN previous."Revision";
  END IF;
  IF revision<>p_expected THEN RAISE EXCEPTION 'processing_revision_conflict'; END IF;
  UPDATE public."ProcessingGuards" SET "Revision"="Revision"+1 WHERE "OrganizationId"=p_org RETURNING "Revision" INTO revision;
  at_time:=clock_timestamp();
  IF p_action='AddContext' THEN
    context_id:=(p_payload->>'ContextId')::uuid; context_version:=(p_payload->>'Version')::bigint;
    IF context_id IS NULL OR context_id='00000000-0000-0000-0000-000000000000' OR context_version IS NULL OR context_version<=0
      OR p_payload->>'ApprovalState' IS NULL OR p_payload->>'ApprovalState' NOT IN ('Unreviewed','SyntheticOnly')
      OR (p_payload->>'EffectiveFrom')::timestamptz IS NULL THEN RAISE EXCEPTION 'invalid_processing_context'; END IF;
    INSERT INTO public."ProcessingContextVersions"("ContextId","Version","OrganizationId","ApprovalState","SyntheticOnly","EffectiveFrom","PurposeRef","RetentionPolicyRef")
      VALUES(context_id,context_version,p_org,p_payload->>'ApprovalState',true,(p_payload->>'EffectiveFrom')::timestamptz,p_payload->>'PurposeRef',p_payload->>'RetentionPolicyRef');
  ELSIF p_action IN ('OpenBinding','CloseBinding','ReplaceBinding') THEN
    course:=p_payload->>'CourseId';
    IF course IS NULL OR btrim(course)='' OR NOT EXISTS(SELECT 1 FROM public."CourseRegistry" WHERE "CourseId"=course AND "OrganizationId"=p_org AND "MappingState"='Verified')
      THEN RAISE EXCEPTION 'processing_course_denied'; END IF;
    IF p_action IN ('CloseBinding','ReplaceBinding') THEN
      binding_from:=(p_payload->>'ExpectedBindingFrom')::timestamptz;
      UPDATE public."CourseContextBindings" SET "EffectiveTo"=at_time
        WHERE "CourseId"=course AND "OrganizationId"=p_org AND "EffectiveFrom"=binding_from AND "EffectiveTo" IS NULL AND "EffectiveFrom"<at_time;
      IF NOT FOUND THEN RAISE EXCEPTION 'processing_binding_conflict'; END IF;
    END IF;
    IF p_action IN ('OpenBinding','ReplaceBinding') THEN
      context_id:=(p_payload->>'ContextId')::uuid; context_version:=(p_payload->>'ContextVersion')::bigint;
      IF p_action='OpenBinding' THEN at_time:=(p_payload->>'EffectiveFrom')::timestamptz; END IF;
      IF context_id IS NULL OR context_version IS NULL OR context_version<=0 OR at_time IS NULL THEN RAISE EXCEPTION 'invalid_processing_binding'; END IF;
      INSERT INTO public."CourseContextBindings"("CourseId","OrganizationId","ContextId","ContextVersion","EffectiveFrom") VALUES(course,p_org,context_id,context_version,at_time);
    END IF;
  ELSIF p_action='AddRestriction' THEN
    context_id:=(p_payload->>'ContextId')::uuid; context_version:=(p_payload->>'ContextVersion')::bigint;
    IF (p_payload->>'RestrictionId')::uuid IS NULL OR (p_payload->>'RestrictionId')::uuid='00000000-0000-0000-0000-000000000000'
      OR context_id IS NULL OR context_version IS NULL OR context_version<=0 OR (p_payload->>'Operations')::integer IS NULL
      THEN RAISE EXCEPTION 'invalid_processing_restriction'; END IF;
    INSERT INTO public."ProcessingRestrictions"("RestrictionId","ContextId","OriginVersion","OrganizationId","SubjectActorId","Operations","ResourceRef","State","Version")
      VALUES((p_payload->>'RestrictionId')::uuid,context_id,context_version,p_org,(p_payload->>'SubjectActorId')::uuid,(p_payload->>'Operations')::integer,p_payload->>'ResourceRef','Active',1);
  ELSIF p_action='WithdrawRestriction' THEN
    UPDATE public."ProcessingRestrictions" SET "State"='Withdrawn',"Version"="Version"+1 WHERE "OrganizationId"=p_org
      AND "RestrictionId"=(p_payload->>'RestrictionId')::uuid AND "Version"=(p_payload->>'ExpectedVersion')::bigint AND "State"='Active';
    IF NOT FOUND THEN RAISE EXCEPTION 'processing_restriction_conflict'; END IF;
  ELSE
    IF p_payload->>'State' IS NULL OR p_payload->>'State' NOT IN ('Enabled','Suspended') THEN RAISE EXCEPTION 'invalid_processing_organization_state'; END IF;
    UPDATE public."Organizations" SET "State"=p_payload->>'State' WHERE "OrganizationId"=p_org;
  END IF;
  INSERT INTO public."SecurityAudit"("AuditId","OrganizationId","Action","SubjectRef","ResourceRef","Result","AuthorityVersion","TraceId","OccurredAt")
    VALUES(audit,p_org,'processing_'||p_action,p_org::text,course,'success',revision,p_operation::text,clock_timestamp());
  INSERT INTO public."ProcessingWriteJournal" VALUES(p_org,p_operation,fingerprint,revision,audit);
  RETURN revision;
END $$;
REVOKE ALL ON FUNCTION public.processing_change(uuid,uuid,bigint,text,jsonb) FROM PUBLIC;

-- Reconciliation never executes the mutation. Absence is not proof of executor quiescence.
CREATE FUNCTION public.processing_find_command(p_org uuid,p_operation uuid,p_expected bigint,p_action text,p_payload jsonb) RETURNS bigint
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
DECLARE previous public."ProcessingWriteJournal"%ROWTYPE; fingerprint text;
BEGIN
  IF p_operation IS NULL OR p_expected IS NULL OR p_payload IS NULL OR p_action IS NULL
    OR jsonb_typeof(p_payload)<>'object' OR octet_length(p_payload::text)>4096 THEN RAISE EXCEPTION 'invalid_processing_command'; END IF;
  PERFORM public.processing_lock(p_org);
  SELECT * INTO previous FROM public."ProcessingWriteJournal" WHERE "OrganizationId"=p_org AND "OperationId"=p_operation;
  IF NOT FOUND THEN RETURN NULL; END IF;
  fingerprint:=encode(sha256(convert_to(jsonb_build_object('action',p_action,'expected',p_expected,'payload',p_payload)::text,'UTF8')),'hex');
  IF previous."Fingerprint"<>fingerprint THEN RAISE EXCEPTION 'processing_operation_conflict' USING ERRCODE='23505'; END IF;
  RETURN previous."Revision";
END $$;
REVOKE ALL ON FUNCTION public.processing_find_command(uuid,uuid,bigint,text,jsonb) FROM PUBLIC;

-- Deployment-owner preflight, not a role grant or a runtime readiness toggle. Rejects
-- inherited/column DML as well as owners and principals able to become a powerful role.
CREATE FUNCTION public.processing_validate_operational_role(p_role regrole) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog AS $$
DECLARE t regclass; candidate oid;
BEGIN
  IF p_role IS NULL OR NOT EXISTS(SELECT 1 FROM pg_roles WHERE oid=p_role) THEN RAISE EXCEPTION 'unsafe_processing_operational_role' USING ERRCODE='42501'; END IF;
  FOR candidate IN SELECT oid FROM pg_roles WHERE pg_has_role(p_role,oid,'MEMBER') LOOP
    IF EXISTS(SELECT 1 FROM pg_roles WHERE oid=candidate AND (rolsuper OR rolcreaterole OR rolcreatedb OR rolreplication OR rolbypassrls
      OR rolname IN ('pg_read_server_files','pg_write_server_files','pg_execute_server_program')))
      OR has_schema_privilege(candidate,'public','CREATE') THEN
      RAISE EXCEPTION 'unsafe_processing_operational_role' USING ERRCODE='42501';
    END IF;
    FOREACH t IN ARRAY ARRAY['public."Organizations"'::regclass,'public."ProcessingGuards"'::regclass,
      'public."ProcessingWriteJournal"'::regclass,'public."ProcessingRestrictions"'::regclass,
      'public."CourseContextBindings"'::regclass,'public."ProcessingContextVersions"'::regclass,'public."SecurityAudit"'::regclass] LOOP
      IF (SELECT relowner FROM pg_class WHERE oid=t)=candidate
        OR has_table_privilege(candidate,t,'INSERT,UPDATE,DELETE,TRUNCATE,TRIGGER')
        OR has_any_column_privilege(candidate,t,'INSERT,UPDATE') THEN
        RAISE EXCEPTION 'operational_processing_DML_or_owner_bypass' USING ERRCODE='42501';
      END IF;
    END LOOP;
  END LOOP;
END $$;
REVOKE ALL ON FUNCTION public.processing_validate_operational_role(regrole) FROM PUBLIC;
