begin;

create table if not exists public.feather_sync (
    user_id uuid primary key references auth.users(id) on delete cascade,
    format_version integer not null check (format_version = 1),
    blob_revision bigint not null check (blob_revision > 0),
    key_bundle jsonb not null check (jsonb_typeof(key_bundle) = 'object'),
    encrypted_blob jsonb not null check (jsonb_typeof(encrypted_blob) = 'object'),
    updated_at timestamptz not null default now()
);

alter table public.feather_sync enable row level security;

revoke all on table public.feather_sync from anon;
revoke all on table public.feather_sync from authenticated;

grant select on table public.feather_sync to authenticated;

drop policy if exists "feather_sync_select_own" on public.feather_sync;

create policy "feather_sync_select_own"
on public.feather_sync
for select
to authenticated
using ((select auth.uid()) = user_id);

create or replace function public.feather_commit_sync(
    p_expected_revision bigint,
    p_format_version integer,
    p_key_bundle jsonb,
    p_encrypted_blob jsonb
)
returns bigint
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    v_user_id uuid;
    v_revision bigint;
begin
    v_user_id := auth.uid();

    if v_user_id is null then
        raise exception 'authentication required'
            using errcode = '42501';
    end if;

    if p_expected_revision < 0 then
        raise exception 'invalid expected revision'
            using errcode = '22023';
    end if;

    if p_format_version <> 1 then
        raise exception 'unsupported format version'
            using errcode = '22023';
    end if;

    if jsonb_typeof(p_key_bundle) <> 'object'
       or jsonb_typeof(p_encrypted_blob) <> 'object' then
        raise exception 'invalid encrypted payload'
            using errcode = '22023';
    end if;

    update public.feather_sync
       set format_version = p_format_version,
           blob_revision = blob_revision + 1,
           key_bundle = p_key_bundle,
           encrypted_blob = p_encrypted_blob,
           updated_at = now()
     where user_id = v_user_id
       and blob_revision = p_expected_revision
     returning blob_revision into v_revision;

    if found then
        return v_revision;
    end if;

    if p_expected_revision = 0 then
        begin
            insert into public.feather_sync (
                user_id,
                format_version,
                blob_revision,
                key_bundle,
                encrypted_blob,
                updated_at
            )
            values (
                v_user_id,
                p_format_version,
                1,
                p_key_bundle,
                p_encrypted_blob,
                now()
            )
            returning blob_revision into v_revision;

            return v_revision;
        exception
            when unique_violation then
                null;
        end;
    end if;

    raise exception 'sync revision conflict'
        using errcode = '40001';
end;
$$;

create or replace function public.feather_delete_sync()
returns void
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    v_user_id uuid;
begin
    v_user_id := auth.uid();

    if v_user_id is null then
        raise exception 'authentication required'
            using errcode = '42501';
    end if;

    delete from public.feather_sync
    where user_id = v_user_id;
end;
$$;

revoke all on function public.feather_commit_sync(bigint, integer, jsonb, jsonb) from public;
revoke all on function public.feather_delete_sync() from public;

grant execute on function public.feather_commit_sync(bigint, integer, jsonb, jsonb) to authenticated;
grant execute on function public.feather_delete_sync() to authenticated;

commit;
