-- Exact raw SQL body from 20261011120000_ProcessingWriterGuardC2G; candidate only.
-- Transactional rollback before use is supported; committed audit/idempotency history must not be erased.
LOCK TABLE public."Organizations" IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE public."ProcessingWriteJournal" IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN
  IF EXISTS(SELECT 1 FROM public."ProcessingWriteJournal") OR EXISTS(SELECT 1 FROM public."ProcessingGuards" WHERE "Revision">0) THEN
    RAISE EXCEPTION 'Processing command history retained; rollback after use requires a separate retention decision';
  END IF;
END $$;
DROP FUNCTION public.processing_validate_operational_role(regrole);
DROP FUNCTION public.processing_find_command(uuid,uuid,bigint,text,jsonb);
DROP FUNCTION public.processing_change(uuid,uuid,bigint,text,jsonb);
DROP FUNCTION public.processing_assert_current(uuid,text,uuid,bigint,timestamptz,uuid[]);
DROP FUNCTION public.processing_begin_write(uuid,bigint);
DROP FUNCTION public.processing_lock(uuid);
DROP TRIGGER processing_organization_guard ON public."Organizations";
DROP FUNCTION public.processing_initialize_guard();
DROP TABLE public."ProcessingWriteJournal";
DROP TABLE public."ProcessingGuards";
