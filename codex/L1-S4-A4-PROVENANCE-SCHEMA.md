# L1/S4-A4 — Physical provenance schema

2026-10-10. Documentation only; synthetic fixtures only. **EXISTING** — подтверждено исходным кодом; **ACCEPTED** — принятая норма PRS1; **PROPOSED** — решение A4, ещё не принято; **NOT PROVEN** — обязательство будущей проверки, не результат теста.

## Решение и совместимость

**PROPOSED:** immutable sidecar `RestrictionSelectorProvenance` (1:1), immutable trusted `Prs1Decisions`, immutable administrative command receipt `Prs1Commands`, protected `RestrictionBirths` и отдельный `Prs1MutationReceipts` для Submit/Review outcome. Educational mutation не требует административного DecisionId. Existing `SecurityAudit` получает secret-free event в том же unit of work. Birth ledger нужен для запрета позднего добавления provenance к legacy; command receipt нужен для atomic pairing и разрешения unknown commit outcome. Это не новая семантика selector.

**EXISTING:** [ProcessingRestriction](../src/Lms.Infrastructure/SchemaFoundations.cs), [SchemaFoundationMappings](../src/Lms.Infrastructure/SchemaFoundationMappings.cs), [SchemaFoundationsM1.Up](../src/Lms.Infrastructure/Migrations/20261009094756_SchemaFoundationsM1.cs) сохраняют immutable ContextId/OriginVersion/OrganizationId/SubjectActorId/ResourceRef/Operations через `s1_restriction_provenance`; State/Version изменяемы. Существующий trigger не проверяет monotone restriction version и не связывает row с provenance. FK origin указывает `(ContextId, Version, OrganizationId)` context, subject — `(OrganizationId, ActorId)` membership. Nullable subject означает всех; его также фиксируем в snapshot.

**Вывод D1:** PostgreSQL может атомарно сохранить новые строки в одной transaction без изменения immutable исторических полей, но существующая модель/контракты сами **не гарантируют** atomic restriction+provenance. Нужны additive tables/constraints, restricted writer contract и единый transaction owner. Простого `SaveChanges` или sidecar FK недостаточно: restriction могла бы commit без sidecar, а legacy — получить его позже. Запретить эти случаи предлагается ниже; миграция не создана.

## Candidate DDL — NOT EXECUTED / NOT VALIDATED

Имена `prs1_owner`, `prs1_writer`, `prs1_reader` — proposed dedicated roles. Owner NOLOGIN, без выдачи owner/superuser/trigger-disable privileges приложению. DDL фрагмент задаёт физическую модель и ключевые guards; deployment grants, trusted procedures и migration packaging — отдельный implementation slice. Существующие identifiers quoted, string snapshots `COLLATE "C"`. Runtime parser дополнительно проверяет всю принятую grammar; SQL prefix/length checks не заменяют parser.

```sql
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
```

`ContractRevision='R1-2026-10-10'` — proposed durable label принятой редакции, не новая revision semantics. UUID nonzero проверяется trusted writer для всех IDs; NOT PROVEN: проверка DDL на целевой версии PostgreSQL и restricted-role fixtures ещё не выполнена; writer обязан reject zero IDs до DML, FK не подменяет этот check. Core DDL намеренно не создаёт роли/миграцию. Future profile требует отдельного schema/reader compatibility decision; unknown persisted revision reader fallback даже если normal writer не может её создать.

## Обязательный protected writer contract

Один internal writer `IssuePrs1`, `ReissuePrs1`, `WithdrawRestriction`; каждый принимает CommandId, immutable approved command, expected old version и authenticated admin capability. Writer **не выдаётся public application role**. Dedicated synthetic harness получает фиксированного admin actor из trusted configuration; входной actor/DecisionRef из educational request не считается evidence. Production admin identity/capability establishment остаётся отдельным owner gate, не начинается S5.

`Prs1Decisions.Evidence`: durable owner authorization identifier, identity/authority of attester, purpose Issue/Reissue/Withdraw, exact proposed selector/org/context/subject/mask/new ID/old ID/expected version, explicit narrowing approval, digest algorithm/version, evidence format version. ApprovedCommand — точная immutable typed payload serialization; алгоритм `SHA-256` над versioned canonical UTF-8 serialization, fixed property order, GUID D lowercase, numbers invariant, no raw floating values. Byte digest — integrity/idempotency, **не доказательство полномочий**. Writer сравнивает и digest, и поля ApprovedCommand с фактическим command, проверяет trusted evidence и canonical parser. Произвольный JSON или существующий FK на Actor не дают trust. При невозможности проверить authority reject.

`Prs1MutationReceipts` используется только educational adapter: digest включает actor/org/action/expected version/canonical request payload; stable command ID известен до начала transaction. Submit хранит созданную SubmissionId, Review — target SubmissionId и созданную ReviewId. Adapter проверяет Review.WorkId/SubmissionId equality, actor и resulting version; FK сам не доказывает все эти равенства. Effects, journal и receipt коммитятся вместе. Existing receipt не разрешает повтор effect или повторную выдачу DTO: новый gate на чтение либо только внутренний committed-status без content. Stored IDs не выдаются неавторизованному caller. Прямой DML ledger запрещён serving roles, immutable history retained. В отличие от Prs1Commands здесь нет admin DecisionId и sidecar pairing.

Порядок внутри [transaction protocol](L1-S4-A4-TRANSACTION-PROTOCOL.md): org lock → decision/capability validation → old row lock/CAS → receipt insert → restriction insert (birth trigger) → sidecar insert → old withdrawal при reissue → SecurityAudit → deferred checks → commit. Receipt may precede sidecar благодаря deferred FK. При любой ошибке rollback всей transaction. Для простого Withdraw receipt не имеет NewRestrictionId, sidecar old не меняется. Новая PRS1 restriction всегда Active/Version=1; withdraw только Active→Withdrawn/Version+1, expected version проверяется UPDATE predicate, resurrection запрещён процедурой. Старый root сохраняет State/Version историю, selector/provenance никогда не редактируются.

**Критически:** таблицы/триггеры выше не являются полной security boundary сами по себе. Нужно отозвать у всех serving roles INSERT/UPDATE/DELETE/TRUNCATE restrictions/provenance/receipts/birth/decisions и право выполнять DDL/отключать triggers, разрешить только проверенные процедуры/isolated writer role. Trusted function owner, fixed search_path, default EXECUTE revoke и grants — проверяемая часть slice. Вызов writer, commit без пары и отдельная поздняя sidecar insertion из application role обязаны fail. Отдельный conservative legacy writer может вставлять restriction без sidecar, но также под org lock/audit; он никогда не создаёт PRS1 receipt. Не вводить globally required sidecar FK для всех restrictions: это сломало бы legacy.

Deferred reciprocal FK и unique CommandId не допускают sidecar без command и command issue без sidecar; deferred trigger проверяет их взаимное соответствие. Birth требует создания root в той же transaction; до расширения schema birth entries нет, backfill ledger запрещён. Snapshot trigger nullable-safe equality защищает subject/ref/org/context/origin/mask. `Restriction.Version` отсутствует в semantic snapshot намеренно: withdrawal не делает provenance invalid. Historical attestation проверяется по immutable decision и доверенному writer origin, а не по текущей educational membership attester; последующий move/revoke не переписывает историю. Reader проверяет durable birth existence и pair consistency, но не сравнивает BirthTransaction с текущим txid: это только insert-time guard. PostgreSQL xid8 — origin-creation witness в этой DB lineage, не глобальная external attestation; selective restores/import запрещены, full consistent restore + reconciliation required.

## Reader classification и corruption

| Состояние | ACCEPTED / PROPOSED поведение |
|---|---|
| Legacy до/после schema expansion, prefix prs1 без provenance | Conservative по current context/subject, независимо ref/bits |
| Valid sidecar + matching immutable snapshot + known profile/revision + trusted decision/command/birth | Только теперь parser, resolution, exact closure/mask evaluation |
| Missing/duplicate/mismatch/untrusted evidence/unknown revision/reserved bits | Conservative; не First(), не strip bits, не silently skip |
| Missing/ambiguous target/chain | Conservative restriction classification или whole operation deny, никогда exact nonmatch |
| DB unavailable; нельзя установить applicability context/subject | Whole operation deny/exception |
| Withdrawn row | Не активный prohibition; её existence не отменяет другие active rows; история retained |

Duplicate sidecar normal DB предотвращает PK; corruption acceptance проверяется fake reader/isolated owner fixture, а не ослаблением production constraints. DBA/superuser tampering не предотвращается ordinary role ACL; integrity/restore tests и operational trust остаются NOT PROVEN. Никаких cascade delete, historical rewriting или автоматической аттестации canonical legacy rows.


**S4-A4 READY FOR OWNER DECISION · PRS1 ACCEPTED · IMPLEMENTATION NOT AUTHORIZED · T04/E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
