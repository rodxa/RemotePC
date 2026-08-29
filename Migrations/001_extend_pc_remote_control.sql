-- RemotePC Supabase migration
-- Run this in the Supabase SQL editor for your existing project.
-- It preserves public.pc_remote_control and existing rows.

alter table public.pc_remote_control
  add column if not exists display_name text,
  add column if not exists tailscale_ip inet,
  add column if not exists enabled boolean not null default true,
  add column if not exists last_seen timestamptz,
  add column if not exists sort_order integer not null default 0,
  add column if not exists updated_at timestamptz not null default now();

do $$
begin
  if exists (
    select 1
      from information_schema.columns
     where table_schema = 'public'
       and table_name = 'pc_remote_control'
       and column_name = 'parsec_peer_id'
  ) and not exists (
    select 1
      from information_schema.columns
     where table_schema = 'public'
       and table_name = 'pc_remote_control'
       and column_name = 'rustdesk_id'
  ) then
    alter table public.pc_remote_control
      rename column parsec_peer_id to rustdesk_id;
  elsif exists (
    select 1
      from information_schema.columns
     where table_schema = 'public'
       and table_name = 'pc_remote_control'
       and column_name = 'parsec_peer_id'
  ) and exists (
    select 1
      from information_schema.columns
     where table_schema = 'public'
       and table_name = 'pc_remote_control'
       and column_name = 'rustdesk_id'
  ) then
    update public.pc_remote_control
       set rustdesk_id = coalesce(rustdesk_id, parsec_peer_id);

    alter table public.pc_remote_control
      drop column parsec_peer_id;
  end if;
end $$;

alter table public.pc_remote_control
  add column if not exists rustdesk_id text;

comment on column public.pc_remote_control.display_name is 'Human-friendly name shown in the RemotePC app.';
comment on column public.pc_remote_control.tailscale_ip is 'Tailscale IP used by the desktop app for remote reachability checks.';
comment on column public.pc_remote_control.rustdesk_id is 'RustDesk ID used by the desktop app to launch the RustDesk client for this PC. Do not store passwords here.';
comment on column public.pc_remote_control.enabled is 'Only enabled rows are visible to the RemotePC app.';
comment on column public.pc_remote_control.last_seen is 'Optional heartbeat timestamp written by a trusted device or future status process.';
comment on column public.pc_remote_control.sort_order is 'Controls display order in the RemotePC app.';
comment on column public.pc_remote_control.updated_at is 'Timestamp updated by wake_pc when a wake command is issued.';

create index if not exists pc_remote_control_enabled_sort_idx
  on public.pc_remote_control (enabled, sort_order, id);

create or replace function public.wake_pc(target_id bigint)
returns bigint
language sql
security definer
set search_path = public
as $$
  update public.pc_remote_control
     set command_id = command_id + 1,
         updated_at = now()
   where id = target_id
     and enabled = true
   returning command_id;
$$;

comment on function public.wake_pc(bigint) is
  'Atomically increments command_id for one enabled PC so the ESP32 can send Wake-on-LAN.';

create or replace function public.add_pc_device(
  p_device_name text,
  p_display_name text default null,
  p_tailscale_ip text default null,
  p_rustdesk_id text default null
)
returns bigint
language plpgsql
security definer
set search_path = public
as $$
declare
  inserted_id bigint;
begin
  if nullif(trim(p_device_name), '') is null then
    raise exception 'device_name is required';
  end if;

  if nullif(trim(p_tailscale_ip), '') is null then
    raise exception 'tailscale_ip is required';
  end if;

  if nullif(trim(p_rustdesk_id), '') is null then
    raise exception 'rustdesk_id is required';
  end if;

  insert into public.pc_remote_control (
    device_name,
    display_name,
    command_id,
    tailscale_ip,
    rustdesk_id,
    enabled,
    sort_order,
    updated_at
  )
  values (
    trim(p_device_name),
    nullif(trim(p_display_name), ''),
    0,
    trim(p_tailscale_ip)::inet,
    regexp_replace(trim(p_rustdesk_id), '\s+', '', 'g'),
    true,
    coalesce((select max(sort_order) + 10 from public.pc_remote_control), 0),
    now()
  )
  returning id into inserted_id;

  return inserted_id;
end;
$$;

comment on function public.add_pc_device(text, text, text, text) is
  'Adds one enabled RemotePC row with safe user-editable fields only. RustDesk passwords must not be stored.';

create or replace function public.update_pc_device(
  p_id bigint,
  p_device_name text,
  p_display_name text default null,
  p_tailscale_ip text default null,
  p_rustdesk_id text default null,
  p_enabled boolean default true
)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if nullif(trim(p_device_name), '') is null then
    raise exception 'device_name is required';
  end if;

  if nullif(trim(p_tailscale_ip), '') is null then
    raise exception 'tailscale_ip is required';
  end if;

  if nullif(trim(p_rustdesk_id), '') is null then
    raise exception 'rustdesk_id is required';
  end if;

  update public.pc_remote_control
     set device_name = trim(p_device_name),
         display_name = nullif(trim(p_display_name), ''),
         tailscale_ip = trim(p_tailscale_ip)::inet,
         rustdesk_id = regexp_replace(trim(p_rustdesk_id), '\s+', '', 'g'),
         enabled = coalesce(p_enabled, true),
         updated_at = now()
   where id = p_id;

  if not found then
    raise exception 'PC row % was not found', p_id;
  end if;
end;
$$;

comment on function public.update_pc_device(bigint, text, text, text, text, boolean) is
  'Updates safe user-editable RemotePC fields. RustDesk passwords must not be stored.';

create or replace function public.delete_pc_device(p_id bigint)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  delete from public.pc_remote_control
   where id = p_id;

  if not found then
    raise exception 'PC row % was not found', p_id;
  end if;
end;
$$;

comment on function public.delete_pc_device(bigint) is
  'Deletes one RemotePC row. It does not change RustDesk, Tailscale, Windows, or ESP32 settings.';

alter table public.pc_remote_control enable row level security;

-- Keep table writes closed to client roles. The desktop app only needs SELECT.
revoke insert, update, delete on public.pc_remote_control from anon, authenticated;
grant select on public.pc_remote_control to anon, authenticated;

-- Functions are executable by PUBLIC unless revoked. Open only the RPC needed by this app.
revoke all on function public.wake_pc(bigint) from public;
grant execute on function public.wake_pc(bigint) to anon, authenticated;

revoke all on function public.add_pc_device(text, text, text, text) from public;
grant execute on function public.add_pc_device(text, text, text, text) to anon, authenticated;

revoke all on function public.update_pc_device(bigint, text, text, text, text, boolean) from public;
grant execute on function public.update_pc_device(bigint, text, text, text, text, boolean) to anon, authenticated;

revoke all on function public.delete_pc_device(bigint) from public;
grant execute on function public.delete_pc_device(bigint) to anon, authenticated;

drop policy if exists "RemotePC can read enabled PCs" on public.pc_remote_control;
drop policy if exists "RemotePC can read PCs" on public.pc_remote_control;
create policy "RemotePC can read PCs"
on public.pc_remote_control
for select
to anon, authenticated
using (true);

-- No UPDATE policy is added intentionally. Wake requests must go through wake_pc(target_id),
-- which changes only command_id and updated_at for the requested enabled PC.

-- Ask PostgREST/Supabase API to refresh its function/schema cache so the RPCs
-- are available through /rest/v1/rpc immediately after running this migration.
notify pgrst, 'reload schema';
