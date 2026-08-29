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

alter table public.pc_remote_control enable row level security;

-- Keep table writes closed to client roles. The desktop app only needs SELECT.
revoke insert, update, delete on public.pc_remote_control from anon, authenticated;
grant select on public.pc_remote_control to anon, authenticated;

-- Functions are executable by PUBLIC unless revoked. Open only the RPC needed by this app.
revoke all on function public.wake_pc(bigint) from public;
grant execute on function public.wake_pc(bigint) to anon, authenticated;

drop policy if exists "RemotePC can read enabled PCs" on public.pc_remote_control;
create policy "RemotePC can read enabled PCs"
on public.pc_remote_control
for select
to anon, authenticated
using (enabled = true);

-- No UPDATE policy is added intentionally. Wake requests must go through wake_pc(target_id),
-- which changes only command_id and updated_at for the requested enabled PC.
