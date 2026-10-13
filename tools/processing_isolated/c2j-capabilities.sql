-- C2-J disposable profile ONLY; internal trusted provisioning LOGIN, never HTTP.
BEGIN;
GRANT USAGE ON SCHEMA public TO c2j_provisioning;
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM c2j_provisioning;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM c2j_provisioning;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public FROM c2j_provisioning;
-- Remove the old S2 column-DML workaround as well as any table DML.
REVOKE INSERT,UPDATE,DELETE ON "Actors","AspNetUsers","ActorIdentities","AccountSecurityState" FROM c2h_runtime;
REVOKE UPDATE ("UserId") ON "AccountSecurityState","ActorIdentities" FROM c2h_runtime;
REVOKE UPDATE ("PasswordHash") ON "AspNetUsers" FROM c2h_runtime;
CREATE OR REPLACE FUNCTION public.s2_create_inactive(p_user uuid,p_actor uuid,p_org uuid,p_name text,p_normalized text,p_at timestamptz)
RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
BEGIN
  IF p_user IS NULL OR p_user='00000000-0000-0000-0000-000000000000'::uuid
    OR p_actor IS NULL OR p_actor='00000000-0000-0000-0000-000000000000'::uuid
    OR p_org IS NULL OR p_org='00000000-0000-0000-0000-000000000000'::uuid
    OR p_name IS NULL OR length(btrim(p_name))=0 OR length(p_name)>256
    OR p_normalized IS NULL OR length(btrim(p_normalized))=0 OR length(p_normalized)>256
    OR p_at IS NULL OR NOT isfinite(p_at) THEN
    RAISE EXCEPTION 'invalid_identity_mapping' USING ERRCODE='22023';
  END IF;
  -- Capability possession is the internal administrative authority. No role flags,
  -- active bit, password, session or educational grants are accepted as inputs.
  -- .NET supplies its existing UpperInvariant normalizer (not locale-dependent SQL upper).
  INSERT INTO public."Actors"("ActorId") VALUES(p_actor) ON CONFLICT ("ActorId") DO NOTHING;
  INSERT INTO public."AspNetUsers"("Id","UserName","NormalizedUserName","SecurityStamp","ConcurrencyStamp",
    "EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount")
    VALUES(p_user,p_name,p_normalized,gen_random_uuid()::text,gen_random_uuid()::text,false,false,false,false,0);
  INSERT INTO public."ActorIdentities"("UserId","ActorId","ProvisioningOrgId","IsActive") VALUES(p_user,p_actor,p_org,false);
  INSERT INTO public."AccountSecurityState"("UserId","State","CredentialEpoch","Version","ChangedAt") VALUES(p_user,'Enabled',1,1,p_at);
  PERFORM public.s2_mapping_audit(p_user,p_at);
END $$;
CREATE OR REPLACE FUNCTION public.s2_lock_account(p_user uuid)
RETURNS SETOF public."AccountSecurityState" LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
DECLARE s public."AccountSecurityState"%ROWTYPE;
BEGIN
  SELECT * INTO s FROM public."AccountSecurityState" WHERE "UserId"=p_user FOR UPDATE;
  IF NOT FOUND THEN RETURN; END IF;
  PERFORM 1 FROM public."AspNetUsers" WHERE "Id"=p_user FOR UPDATE;
  PERFORM 1 FROM public."ActorIdentities" WHERE "UserId"=p_user FOR UPDATE;
  RETURN NEXT s;
END $$;
CREATE OR REPLACE FUNCTION public.s2_rehash_password(p_user uuid,p_expected text,p_replacement text)
RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
BEGIN
  PERFORM public.s2_lock_account(p_user);
  IF p_expected IS NULL OR p_replacement IS NULL OR length(p_replacement)=0 OR octet_length(p_replacement)>1024
    OR NOT EXISTS(SELECT 1 FROM public."AccountSecurityState" s JOIN public."ActorIdentities" a USING("UserId")
      WHERE s."UserId"=p_user AND s."State"='Enabled' AND a."IsActive") THEN
    RAISE EXCEPTION 'invalid_rehash' USING ERRCODE='42501';
  END IF;
  UPDATE public."AspNetUsers" SET "PasswordHash"=p_replacement WHERE "Id"=p_user AND "PasswordHash"=p_expected;
  IF NOT FOUND THEN RAISE EXCEPTION 'rehash_conflict' USING ERRCODE='40001'; END IF;
END $$;
REVOKE ALL ON FUNCTION public.s2_create_inactive(uuid,uuid,uuid,text,text,timestamptz),
  public.s2_lock_account(uuid),public.s2_rehash_password(uuid,text,text) FROM PUBLIC,c2h_runtime,c2h_processing,c2j_provisioning;
GRANT EXECUTE ON FUNCTION public.s2_create_inactive(uuid,uuid,uuid,text,text,timestamptz) TO c2j_provisioning;
GRANT EXECUTE ON FUNCTION public.s2_lock_account(uuid),public.s2_rehash_password(uuid,text,text) TO c2h_runtime;
SELECT public.processing_validate_operational_role('c2h_runtime');
SELECT public.processing_validate_operational_role('c2h_processing');
SELECT public.processing_validate_operational_role('c2j_provisioning');
COMMIT;
