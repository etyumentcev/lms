-- C2-I candidate ONLY. Apply as c2h_owner in the disposable C2-H database.
-- No production migration or role expansion. Caller owns the transaction.
BEGIN;
CREATE OR REPLACE FUNCTION public.s2_session_audit(p_session uuid,p_action text,p_at timestamptz)
RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
DECLARE s public."IdentitySessions"%ROWTYPE;
BEGIN
  IF p_session IS NULL OR p_action IS NULL OR octet_length(p_action)>32 OR p_at IS NULL OR NOT isfinite(p_at)
     OR p_action NOT IN ('session_created','session_rotated','session_unavailable','refresh_replay','session_revoked','sessions_revoked') THEN
    RAISE EXCEPTION 'invalid_s2_audit' USING ERRCODE='42501';
  END IF;
  -- Only a session version written by this transaction is an eligible source.
  -- Isolated adapters disable EF automatic savepoints on their dedicated context;
  -- any failure escapes and disposes/rolls back the entire owning transaction.
  -- No caller-supplied actor, organization, subject, result, context or evidence.
  SELECT * INTO s FROM public."IdentitySessions" WHERE "SessionId"=p_session
    AND xmin::text=pg_current_xact_id()::xid::text;
  IF NOT FOUND THEN RAISE EXCEPTION 'invalid_s2_audit_source' USING ERRCODE='42501'; END IF;
  IF (p_action='session_created' AND (s."Generation"<>1 OR s."RevokedAt" IS NOT NULL OR p_at<>s."CreatedAt"))
     OR (p_action='session_rotated' AND (s."Generation"<=1 OR s."RevokedAt" IS NOT NULL OR p_at<>s."LastSuccessfulRefreshAt"))
     OR (p_action NOT IN ('session_created','session_rotated') AND
       (s."RevokedAt" IS NULL OR s."RevocationReasonRef" IS DISTINCT FROM p_action OR s."RevokedAt"<>greatest(p_at,s."CreatedAt"))) THEN
    RAISE EXCEPTION 'invalid_s2_audit_state' USING ERRCODE='42501';
  END IF;
  INSERT INTO public."SecurityAudit"("AuditId","Action","SubjectRef","ResourceRef","Result","AuthorityVersion","TraceId","OccurredAt")
    VALUES(gen_random_uuid(),p_action,s."UserId"::text,s."SessionId"::text,'success',s."Generation",gen_random_uuid()::text,p_at);
END $$;
CREATE OR REPLACE FUNCTION public.s2_mapping_audit(p_user uuid,p_at timestamptz)
RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
DECLARE a public."ActorIdentities"%ROWTYPE;
BEGIN
  IF p_user IS NULL OR p_at IS NULL OR NOT isfinite(p_at) THEN
    RAISE EXCEPTION 'invalid_s2_mapping_audit' USING ERRCODE='42501';
  END IF;
  SELECT * INTO a FROM public."ActorIdentities" WHERE "UserId"=p_user AND NOT "IsActive"
    AND xmin::text=pg_current_xact_id()::xid::text;
  IF NOT FOUND OR NOT EXISTS(SELECT 1 FROM public."AspNetUsers" WHERE "Id"=p_user
       AND "PasswordHash" IS NULL AND xmin::text=pg_current_xact_id()::xid::text)
     OR NOT EXISTS(SELECT 1 FROM public."AccountSecurityState" WHERE "UserId"=p_user
       AND "State"='Enabled' AND "CredentialEpoch"=1 AND "Version"=1
       AND xmin::text=pg_current_xact_id()::xid::text) THEN
    RAISE EXCEPTION 'invalid_s2_mapping_source' USING ERRCODE='42501';
  END IF;
  INSERT INTO public."SecurityAudit"("AuditId","ActorId","OrganizationId","Action","SubjectRef","Result","TraceId","OccurredAt")
    VALUES(gen_random_uuid(),a."ActorId",a."ProvisioningOrgId",'identity_mapping_created',p_user::text,'inactive',gen_random_uuid()::text,p_at);
END $$;
CREATE OR REPLACE FUNCTION public.s2_lock_issuing_organization(p_org uuid)
RETURNS SETOF public."Organizations" LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
DECLARE o public."Organizations"%ROWTYPE;
BEGIN
  -- Same FOR UPDATE and duration as the original issuer, including ineligible rows.
  SELECT * INTO o FROM public."Organizations" WHERE "OrganizationId"=p_org FOR UPDATE;
  IF FOUND AND o."State"='Enabled' AND o."SyntheticOnly" THEN RETURN NEXT o; END IF;
END $$;
REVOKE ALL ON FUNCTION public.s2_session_audit(uuid,text,timestamptz),public.s2_mapping_audit(uuid,timestamptz),
  public.s2_lock_issuing_organization(uuid) FROM PUBLIC,c2h_runtime,c2h_processing;
-- Mapping creation remains deployment-only (H03). Do not expose its audit writer
-- to a runtime that can only update existing identity rows.
GRANT EXECUTE ON FUNCTION public.s2_session_audit(uuid,text,timestamptz),
  public.s2_lock_issuing_organization(uuid) TO c2h_runtime;
SELECT public.processing_validate_operational_role('c2h_runtime');
SELECT public.processing_validate_operational_role('c2h_processing');
COMMIT;
