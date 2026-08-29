-- RemotePC Supabase migration
-- Run this in the Supabase SQL editor for your existing project.
-- It preserves public.pc_remote_control and existing rows.

alter table public.pc_remote_control
  add column if not exists display_name text,
  add column if not exists tailscale_ip inet,
  add column if not exists parsec_peer_id text,
  add column if not exists enabled boolean not null default true,
  add column if not exists last_seen timestamptz,
  add column if not exists sort_order integer not null default 0;

comment on column public.pc_remote_control.display_name is 'Human-friendly name shown in the RemotePC app.';
comment on column public.pc_remote_control.tailscale_ip is 'Tailscale IP used by the desktop app for remote reachability checks.';
comment on column public.pc_remote_control.parsec_peer_id is 'Optional Parsec peer identifier reserved for future direct peer launch support.';
comment on column public.pc_remote_control.enabled is 'Only enabled rows are visible to the RemotePC app.';
comment on column public.pc_remote_control.last_seen is 'Optional heartbeat timestamp written by a trusted device or future status process.';
comment on column public.pc_remote_control.sort_order is 'Controls display order in the RemotePC app.';

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
