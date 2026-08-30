-- RemotePC host-mode and custom actions migration.
-- Safe for existing public.pc_remote_control rows. No raw host secrets are stored here.

alter table public.pc_remote_control
  add column if not exists remote_port integer not null default 47632,
  add column if not exists remote_enabled boolean not null default false,
  add column if not exists remote_device_id uuid,
  add column if not exists remote_version text;

alter table public.pc_remote_control
  drop constraint if exists pc_remote_control_remote_port_check;

alter table public.pc_remote_control
  add constraint pc_remote_control_remote_port_check
  check (remote_port between 1 and 65535);

comment on column public.pc_remote_control.remote_port is 'RemotePC host listener port. Default is 47632 but clients should use this value.';
comment on column public.pc_remote_control.remote_enabled is 'Whether the matched RemotePC install reports host mode enabled.';
comment on column public.pc_remote_control.remote_device_id is 'Non-secret RemotePC installation id. Raw host/pairing secrets are never stored in Supabase.';
comment on column public.pc_remote_control.remote_version is 'RemotePC host application version reported by the desktop app.';

create table if not exists public.pc_commands (
  id bigint generated always as identity primary key,
  pc_id bigint not null references public.pc_remote_control(id) on delete cascade,
  name text not null,
  description text,
  category text,
  command_type text not null,
  command text,
  arguments text,
  working_directory text,
  require_confirmation boolean not null default true,
  timeout_seconds integer not null default 30,
  enabled boolean not null default true,
  sort_order integer not null default 0,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint pc_commands_name_not_blank check (length(trim(name)) > 0),
  constraint pc_commands_type_check check (command_type in ('builtin', 'powershell', 'process')),
  constraint pc_commands_timeout_check check (timeout_seconds between 1 and 3600)
);

create index if not exists pc_commands_pc_sort_idx
  on public.pc_commands (pc_id, category, sort_order, id);

create index if not exists pc_commands_enabled_idx
  on public.pc_commands (pc_id, enabled);

comment on table public.pc_commands is 'Saved machine-specific RemotePC actions. Remote callers execute by action id only.';
comment on column public.pc_commands.command is 'For powershell this is the saved script text; for process this is the saved executable path. The host never accepts arbitrary command text over HTTP.';

create or replace function public.set_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

drop trigger if exists pc_commands_set_updated_at on public.pc_commands;
create trigger pc_commands_set_updated_at
before update on public.pc_commands
for each row execute function public.set_updated_at();

create or replace function public.update_pc_remote_metadata(
  p_id bigint,
  p_remote_enabled boolean,
  p_remote_port integer,
  p_remote_device_id uuid,
  p_remote_version text default null
)
returns void
language plpgsql
security definer
set search_path = public
as $$
begin
  if p_remote_port is null or p_remote_port < 1 or p_remote_port > 65535 then
    raise exception 'remote_port must be between 1 and 65535';
  end if;

  update public.pc_remote_control
     set remote_enabled = coalesce(p_remote_enabled, false),
         remote_port = p_remote_port,
         remote_device_id = p_remote_device_id,
         remote_version = nullif(trim(p_remote_version), ''),
         updated_at = now()
   where id = p_id;

  if not found then
    raise exception 'PC row % was not found', p_id;
  end if;
end;
$$;

comment on function public.update_pc_remote_metadata(bigint, boolean, integer, uuid, text) is
  'Publishes non-secret host metadata for one RemotePC row. Pairing/auth secrets remain local and DPAPI-protected.';

alter table public.pc_commands enable row level security;

revoke all on public.pc_commands from anon, authenticated;
grant select, insert, update, delete on public.pc_commands to anon;
grant select, insert, update, delete on public.pc_commands to authenticated;
grant usage, select on sequence public.pc_commands_id_seq to anon;
grant usage, select on sequence public.pc_commands_id_seq to authenticated;

drop policy if exists "RemotePC anon can manage actions" on public.pc_commands;
drop policy if exists "RemotePC authenticated users can read actions" on public.pc_commands;
drop policy if exists "RemotePC authenticated users can insert actions" on public.pc_commands;
drop policy if exists "RemotePC authenticated users can update actions" on public.pc_commands;
drop policy if exists "RemotePC authenticated users can delete actions" on public.pc_commands;

-- This desktop app currently uses a publishable key without Supabase Auth, so anon
-- can manage saved action definitions. This is acceptable only for a trusted
-- personal project. For production or shared use, add Supabase Auth ownership
-- columns and replace this policy with owner checks.
create policy "RemotePC anon can manage actions"
on public.pc_commands for all
to anon
using (true)
with check (true);

create policy "RemotePC authenticated users can read actions"
on public.pc_commands for select
to authenticated
using (true);

create policy "RemotePC authenticated users can insert actions"
on public.pc_commands for insert
to authenticated
with check (true);

create policy "RemotePC authenticated users can update actions"
on public.pc_commands for update
to authenticated
using (true)
with check (true);

create policy "RemotePC authenticated users can delete actions"
on public.pc_commands for delete
to authenticated
using (true);

revoke all on function public.update_pc_remote_metadata(bigint, boolean, integer, uuid, text) from public;
grant execute on function public.update_pc_remote_metadata(bigint, boolean, integer, uuid, text) to anon, authenticated;

notify pgrst, 'reload schema';
