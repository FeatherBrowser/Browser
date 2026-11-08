begin;

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
    v_source_device_id uuid;
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

    delete from public.feather_device_transfers as transfer
    where transfer.user_id = v_user_id
      and transfer.target_device_id = p_target_device_id
    returning transfer.source_device_id into v_source_device_id;

    if v_source_device_id is null then
        raise exception 'no approval transfer exists for this device'
            using errcode = '22023';
    end if;

    if not exists (
        select 1
        from public.feather_devices
        where user_id = v_user_id
          and device_id = v_source_device_id
          and status = 'approved'
    ) then
        raise exception 'the approving device is not approved'
            using errcode = '42501';
    end if;

    update public.feather_devices
    set status = 'approved',
        last_seen_at = now()
    where user_id = v_user_id
      and device_id = p_target_device_id
      and status = 'pending';

    if not found then
        raise exception 'the device is not waiting for approval'
            using errcode = '22023';
    end if;
end;
$$;

create or replace function public.feather_register_recovered_device(
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
        raise exception 'sync must exist before registering a recovered device'
            using errcode = '22023';
    end if;

    if exists (
        select 1
        from public.feather_devices
        where user_id = v_user_id
          and device_id = p_device_id
          and status = 'approved'
          and public_key <> p_public_key
    ) then
        raise exception 'an approved device with this id already uses a different key'
            using errcode = '42501';
    end if;

    insert into public.feather_devices (
        user_id,
        device_id,
        device_name,
        public_key,
        status,
        last_seen_at
    )
    values (
        v_user_id,
        p_device_id,
        left(trim(p_device_name), 80),
        p_public_key,
        'approved',
        now()
    )
    on conflict (user_id, device_id) do update
    set device_name = excluded.device_name,
        public_key = excluded.public_key,
        status = 'approved',
        last_seen_at = now();
end;
$$;

revoke all on function public.feather_consume_device_transfer(uuid) from public;
revoke all on function public.feather_register_recovered_device(uuid, text, text) from public;
grant execute on function public.feather_consume_device_transfer(uuid) to authenticated;
grant execute on function public.feather_register_recovered_device(uuid, text, text) to authenticated;

commit;
