-- RemotePC multi-PC Wake-on-LAN migration.
-- Safe for existing public.pc_remote_control rows. It keeps ESP32 access read-only.

alter table public.pc_remote_control
  add column if not exists mac_address text,
  add column if not exists wake_agent text default 'home',
  add column if not exists wol_port integer not null default 9;

update public.pc_remote_control
   set wake_agent = 'home'
 where wake_agent is null
    or length(trim(wake_agent)) = 0;

update public.pc_remote_control
   set wol_port = 9
 where wol_port is null
    or wol_port < 1
    or wol_port > 65535;

alter table public.pc_remote_control
  alter column wake_agent set default 'home',
  alter column wol_port set default 9,
  alter column wol_port set not null;

alter table public.pc_remote_control
  drop constraint if exists pc_remote_control_wol_port_check;

alter table public.pc_remote_control
  add constraint pc_remote_control_wol_port_check
  check (wol_port between 1 and 65535);

alter table public.pc_remote_control
  drop constraint if exists pc_remote_control_wake_agent_not_blank_check;

alter table public.pc_remote_control
  add constraint pc_remote_control_wake_agent_not_blank_check
  check (wake_agent is null or length(trim(wake_agent)) > 0);

alter table public.pc_remote_control
  drop constraint if exists pc_remote_control_mac_address_format_check;

do $$
begin
  if not exists (
    select 1
      from public.pc_remote_control
     where mac_address is not null
       and trim(mac_address) !~* '^[0-9a-f]{2}([:-][0-9a-f]{2}){5}$'
  ) then
    alter table public.pc_remote_control
      add constraint pc_remote_control_mac_address_format_check
      check (
        mac_address is null
        or trim(mac_address) ~* '^[0-9a-f]{2}([:-][0-9a-f]{2}){5}$'
      );
  else
    raise notice 'Skipping pc_remote_control_mac_address_format_check because existing rows contain malformed non-null MAC addresses.';
  end if;
end $$;

comment on column public.pc_remote_control.mac_address is 'Wake-on-LAN MAC address. Prefer canonical uppercase colon format, for example 9C:6B:00:7B:DC:44.';
comment on column public.pc_remote_control.wake_agent is 'Non-secret routing label used by ESP32 wake agents, for example home or office.';
comment on column public.pc_remote_control.wol_port is 'UDP destination port for Wake-on-LAN magic packets. Usually 9.';

create index if not exists pc_remote_control_wake_agent_enabled_idx
  on public.pc_remote_control (wake_agent, enabled, id)
  where mac_address is not null;

update public.pc_remote_control
   set mac_address = coalesce(mac_address, '9C:6B:00:7B:DC:44'),
       wake_agent = coalesce(nullif(trim(wake_agent), ''), 'home'),
       wol_port = coalesce(wol_port, 9)
 where device_name = 'home-pc';

drop function if exists public.add_pc_device(text, text, text, text, boolean, boolean, integer, uuid);
create or replace function public.add_pc_device(
  p_device_name text,
  p_display_name text default null,
  p_tailscale_ip text default null,
  p_rustdesk_id text default null,
  p_enabled boolean default true,
  p_remote_enabled boolean default false,
  p_remote_port integer default 47632,
  p_remote_device_id uuid default null,
  p_mac_address text default null,
  p_wake_agent text default 'home',
  p_wol_port integer default 9
)
returns bigint
language plpgsql
security definer
set search_path = public
as $$
declare
  inserted_id bigint;
  normalized_mac text;
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

  if p_remote_port is null or p_remote_port < 1 or p_remote_port > 65535 then
    raise exception 'remote_port must be between 1 and 65535';
  end if;

  if p_wol_port is null or p_wol_port < 1 or p_wol_port > 65535 then
    raise exception 'wol_port must be between 1 and 65535';
  end if;

  normalized_mac := nullif(replace(upper(trim(coalesce(p_mac_address, ''))), '-', ':'), '');

  if normalized_mac is not null and normalized_mac !~ '^[0-9A-F]{2}(:[0-9A-F]{2}){5}$' then
    raise exception 'mac_address must look like 9C:6B:00:7B:DC:44';
  end if;

  insert into public.pc_remote_control (
    device_name,
    display_name,
    command_id,
    tailscale_ip,
    rustdesk_id,
    enabled,
    sort_order,
    updated_at,
    remote_enabled,
    remote_port,
    remote_device_id,
    mac_address,
    wake_agent,
    wol_port
  )
  values (
    trim(p_device_name),
    nullif(trim(p_display_name), ''),
    0,
    trim(p_tailscale_ip)::inet,
    regexp_replace(trim(p_rustdesk_id), '\s+', '', 'g'),
    coalesce(p_enabled, true),
    coalesce((select max(sort_order) + 10 from public.pc_remote_control), 0),
    now(),
    coalesce(p_remote_enabled, false),
    p_remote_port,
    p_remote_device_id,
    normalized_mac,
    coalesce(nullif(trim(p_wake_agent), ''), 'home'),
    p_wol_port
  )
  returning id into inserted_id;

  return inserted_id;
end;
$$;

comment on function public.add_pc_device(text, text, text, text, boolean, boolean, integer, uuid, text, text, integer) is
  'Adds one RemotePC row with safe user-editable fields, optional host metadata, and Wake-on-LAN routing metadata.';

drop function if exists public.update_pc_device(bigint, text, text, text, text, boolean, boolean, integer, uuid);
create or replace function public.update_pc_device(
  p_id bigint,
  p_device_name text,
  p_display_name text default null,
  p_tailscale_ip text default null,
  p_rustdesk_id text default null,
  p_enabled boolean default true,
  p_remote_enabled boolean default false,
  p_remote_port integer default 47632,
  p_remote_device_id uuid default null,
  p_mac_address text default null,
  p_wake_agent text default 'home',
  p_wol_port integer default 9
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  normalized_mac text;
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

  if p_remote_port is null or p_remote_port < 1 or p_remote_port > 65535 then
    raise exception 'remote_port must be between 1 and 65535';
  end if;

  if p_wol_port is null or p_wol_port < 1 or p_wol_port > 65535 then
    raise exception 'wol_port must be between 1 and 65535';
  end if;

  normalized_mac := nullif(replace(upper(trim(coalesce(p_mac_address, ''))), '-', ':'), '');

  if normalized_mac is not null and normalized_mac !~ '^[0-9A-F]{2}(:[0-9A-F]{2}){5}$' then
    raise exception 'mac_address must look like 9C:6B:00:7B:DC:44';
  end if;

  update public.pc_remote_control
     set device_name = trim(p_device_name),
         display_name = nullif(trim(p_display_name), ''),
         tailscale_ip = trim(p_tailscale_ip)::inet,
         rustdesk_id = regexp_replace(trim(p_rustdesk_id), '\s+', '', 'g'),
         enabled = coalesce(p_enabled, true),
         remote_enabled = coalesce(p_remote_enabled, false),
         remote_port = p_remote_port,
         remote_device_id = p_remote_device_id,
         mac_address = normalized_mac,
         wake_agent = coalesce(nullif(trim(p_wake_agent), ''), 'home'),
         wol_port = p_wol_port,
         updated_at = now()
   where id = p_id;

  if not found then
    raise exception 'PC row % was not found', p_id;
  end if;
end;
$$;

comment on function public.update_pc_device(bigint, text, text, text, text, boolean, boolean, integer, uuid, text, text, integer) is
  'Updates safe user-editable RemotePC fields, optional host metadata, and Wake-on-LAN routing metadata.';

grant select on public.pc_remote_control to anon, authenticated;

revoke all on function public.add_pc_device(text, text, text, text, boolean, boolean, integer, uuid, text, text, integer) from public;
grant execute on function public.add_pc_device(text, text, text, text, boolean, boolean, integer, uuid, text, text, integer) to anon, authenticated;

revoke all on function public.update_pc_device(bigint, text, text, text, text, boolean, boolean, integer, uuid, text, text, integer) from public;
grant execute on function public.update_pc_device(bigint, text, text, text, text, boolean, boolean, integer, uuid, text, text, integer) to anon, authenticated;

notify pgrst, 'reload schema';
