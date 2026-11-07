begin;


drop policy if exists "feather_sync_require_aal2" on public.feather_sync;

create policy "feather_sync_require_aal2"
on public.feather_sync
as restrictive
for select
to authenticated
using (
    coalesce((select auth.jwt()->>'aal'), 'aal1') = 'aal2'
);

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

    if coalesce(auth.jwt()->>'aal', 'aal1') <> 'aal2' then
        raise exception 'multi-factor authentication required'
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

    if coalesce(auth.jwt()->>'aal', 'aal1') <> 'aal2' then
        raise exception 'multi-factor authentication required'
            using errcode = '42501';
    end if;

    delete from public.feather_sync
    where user_id = v_user_id;
end;
$$;

create table if not exists public.feather_devices (
    user_id uuid not null references auth.users(id) on delete cascade,
    device_id uuid not null,
    device_name text not null
        check (char_length(device_name) between 1 and 80),
    public_key text not null
        check (char_length(public_key) between 80 and 1024),
    status text not null
        check (status in ('pending', 'approved', 'revoked')),
    created_at timestamptz not null default now(),
    last_seen_at timestamptz not null default now(),
    primary key (user_id, device_id),
    unique (user_id, public_key)
);

alter table public.feather_devices enable row level security;

revoke all on table public.feather_devices from anon;
revoke all on table public.feather_devices from authenticated;
grant select on table public.feather_devices to authenticated;

drop policy if exists "feather_devices_select_own" on public.feather_devices;
drop policy if exists "feather_devices_require_aal2" on public.feather_devices;

create policy "feather_devices_select_own"
on public.feather_devices
for select
to authenticated
using ((select auth.uid()) = user_id);

create policy "feather_devices_require_aal2"
on public.feather_devices
as restrictive
for select
to authenticated
using (
    coalesce((select auth.jwt()->>'aal'), 'aal1') = 'aal2'
);

create table if not exists public.feather_device_transfers (
    user_id uuid not null references auth.users(id) on delete cascade,
    target_device_id uuid not null,
    source_device_id uuid not null,
    source_public_key text not null,
    key_bundle_fingerprint text not null
        check (key_bundle_fingerprint ~ '^[A-F0-9]{64}$'),
    salt text not null,
    nonce text not null,
    tag text not null,
    encrypted_master_key text not null,
    created_at timestamptz not null default now(),
    primary key (user_id, target_device_id),
    foreign key (user_id, target_device_id)
        references public.feather_devices(user_id, device_id)
        on delete cascade,
    foreign key (user_id, source_device_id)
        references public.feather_devices(user_id, device_id)
        on delete cascade
);

alter table public.feather_device_transfers enable row level security;

revoke all on table public.feather_device_transfers from anon;
revoke all on table public.feather_device_transfers from authenticated;
grant select on table public.feather_device_transfers to authenticated;

drop policy if exists "feather_device_transfers_select_own" on public.feather_device_transfers;
drop policy if exists "feather_device_transfers_require_aal2" on public.feather_device_transfers;

create policy "feather_device_transfers_select_own"
on public.feather_device_transfers
for select
to authenticated
using ((select auth.uid()) = user_id);

create policy "feather_device_transfers_require_aal2"
on public.feather_device_transfers
as restrictive
for select
to authenticated
using (
    coalesce((select auth.jwt()->>'aal'), 'aal1') = 'aal2'
);

create or replace function public.feather_register_first_device(
    p_device_id uuid,
    p_device_name text,
    p_public_key text
)
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

    if coalesce(auth.jwt()->>'aal', 'aal1') <> 'aal2' then
        raise exception 'multi-factor authentication required'
            using errcode = '42501';
    end if;

    if not exists (
        select 1
        from public.feather_sync
        where user_id = v_user_id
    ) then
        raise exception 'sync must be initialized first'
            using errcode = '22023';
    end if;

    if exists (
        select 1
        from public.feather_devices
        where user_id = v_user_id
          and status = 'approved'
    ) then
        raise exception 'an approved device already exists'
            using errcode = '23505';
    end if;

    insert into public.feather_devices (
        user_id,
        device_id,
        device_name,
        public_key,
        status
    )
    values (
        v_user_id,
        p_device_id,
        left(trim(p_device_name), 80),
        p_public_key,
        'approved'
    );
end;
$$;

create or replace function public.feather_register_pending_device(
    p_device_id uuid,
    p_device_name text,
    p_public_key text
)
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

    if coalesce(auth.jwt()->>'aal', 'aal1') <> 'aal2' then
        raise exception 'multi-factor authentication required'
            using errcode = '42501';
    end if;

    insert into public.feather_devices (
        user_id,
        device_id,
        device_name,
        public_key,
        status
    )
    values (
        v_user_id,
        p_device_id,
        left(trim(p_device_name), 80),
        p_public_key,
        'pending'
    )
    on conflict (user_id, device_id) do update
    set device_name = excluded.device_name,
        last_seen_at = now()
    where public.feather_devices.public_key = excluded.public_key
      and public.feather_devices.status = 'pending';
end;
$$;

create or replace function public.feather_submit_device_transfer(
    p_source_device_id uuid,
    p_target_device_id uuid,
    p_source_public_key text,
    p_key_bundle_fingerprint text,
    p_salt text,
    p_nonce text,
    p_tag text,
    p_encrypted_master_key text
)
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

    if coalesce(auth.jwt()->>'aal', 'aal1') <> 'aal2' then
        raise exception 'multi-factor authentication required'
            using errcode = '42501';
    end if;

    if p_source_device_id = p_target_device_id then
        raise exception 'source and target devices must differ'
            using errcode = '22023';
    end if;

    if not exists (
        select 1
        from public.feather_devices
        where user_id = v_user_id
          and device_id = p_source_device_id
          and status = 'approved'
          and public_key = p_source_public_key
    ) then
        raise exception 'source device is not approved'
            using errcode = '42501';
    end if;

    if not exists (
        select 1
        from public.feather_devices
        where user_id = v_user_id
          and device_id = p_target_device_id
          and status = 'pending'
    ) then
        raise exception 'target device is not pending'
            using errcode = '22023';
    end if;

    insert into public.feather_device_transfers (
        user_id,
        target_device_id,
        source_device_id,
        source_public_key,
        key_bundle_fingerprint,
        salt,
        nonce,
        tag,
        encrypted_master_key
    )
    values (
        v_user_id,
        p_target_device_id,
        p_source_device_id,
        p_source_public_key,
        p_key_bundle_fingerprint,
        p_salt,
        p_nonce,
        p_tag,
        p_encrypted_master_key
    )
    on conflict (user_id, target_device_id) do update
    set source_device_id = excluded.source_device_id,
        source_public_key = excluded.source_public_key,
        key_bundle_fingerprint = excluded.key_bundle_fingerprint,
        salt = excluded.salt,
        nonce = excluded.nonce,
        tag = excluded.tag,
        encrypted_master_key = excluded.encrypted_master_key,
        created_at = now();

end;
$$;

create or replace function public.feather_consume_device_transfer(
    p_target_device_id uuid
)
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

    if coalesce(auth.jwt()->>'aal', 'aal1') <> 'aal2' then
        raise exception 'multi-factor authentication required'
            using errcode = '42501';
    end if;

    delete from public.feather_device_transfers
    where user_id = v_user_id
      and target_device_id = p_target_device_id;

    update public.feather_devices
    set status = 'approved',
        last_seen_at = now()
    where user_id = v_user_id
      and device_id = p_target_device_id
      and status = 'pending';
end;
$$;

revoke all on function public.feather_register_first_device(uuid, text, text) from public;
revoke all on function public.feather_register_pending_device(uuid, text, text) from public;
revoke all on function public.feather_submit_device_transfer(uuid, uuid, text, text, text, text, text, text) from public;
revoke all on function public.feather_consume_device_transfer(uuid) from public;

grant execute on function public.feather_register_first_device(uuid, text, text) to authenticated;
grant execute on function public.feather_register_pending_device(uuid, text, text) to authenticated;
grant execute on function public.feather_submit_device_transfer(uuid, uuid, text, text, text, text, text, text) to authenticated;
grant execute on function public.feather_consume_device_transfer(uuid) to authenticated;

commit;
