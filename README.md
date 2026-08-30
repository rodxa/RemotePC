# RemotePC

RemotePC is one Avalonia desktop app that can be a controller, a tray host, or both. Wake still goes through Supabase and the ESP32-S3. Remote desktop still opens RustDesk. Host commands travel directly to another running RemotePC instance over Tailscale.

## Architecture

```text
Controller RemotePC
  |-- Supabase -> ESP32-S3 -> Wake-on-LAN
  |-- RustDesk -> remote desktop
  `-- Tailscale HTTP -> Host RemotePC -> built-in/custom actions
```

There is no Windows Service and no separate agent executable. Host mode starts only after a Windows user logs in. RustDesk should handle login-screen access after wake/reboot; RemotePC host commands become available after the user session starts.

## Settings

Settings are stored in `appsettings.json` next to the executable during publish, or in the project folder during development.

```json
{
  "Supabase": {
    "Url": "https://YOUR_PROJECT.supabase.co",
    "PublishableKey": "YOUR_PUBLISHABLE_KEY"
  },
  "Local": {
    "StartWithWindows": false,
    "StartMinimized": false,
    "CloseToTray": true,
    "RemoteControlEnabled": false,
    "NotificationsEnabled": true,
    "MachineName": "Main PC",
    "RemotePort": 47632
  }
}
```

Use a Supabase publishable key only. Never put a `service_role` key, RustDesk password, host token, or pairing secret in this file.

## Tray And Startup

RemotePC creates an Avalonia tray icon with:

- Open RemotePC
- Host: Enabled/Disabled
- Status
- Exit

Closing or minimizing the main window hides it to the tray when `CloseToTray` is enabled. `Exit` is the explicit shutdown path and stops the in-process host. `StartWithWindows` writes a per-user registry value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with the current executable path and `--background`, so no administrator permission is required. `StartMinimized` or `--background` starts hidden in the tray.

## Single Instance

The app uses a per-user named mutex and named pipe. If RemotePC is already running hidden in the tray and the user launches it again, the new process sends `open` to the existing process and exits. The existing instance restores the main window, so duplicate host servers are not created.

## Host Mode

When `RemoteControlEnabled` is true, RemotePC starts a Kestrel server inside `RemotePC.exe`. It exposes:

- `GET /api/health`
- `POST /api/auth/token`
- `POST /api/builtin/shutdown`
- `POST /api/builtin/restart`
- `POST /api/builtin/lock`
- `POST /api/actions/{id}`

The host tries to bind to the local Tailscale IPv4. If Tailscale cannot be detected reliably, it binds to all IPv4 interfaces and logs the limitation. Keep the firewall rule restricted to Tailscale/private traffic. Clients use each PC row's `tailscale_ip` and `remote_port`; no public IP or router forwarding is used.

## Remote Command Password And Credentials

Each install gets a local device id and a random 256-bit host token. They are stored in `%APPDATA%\RemotePC\credentials.json` encrypted with Windows DPAPI for the current user. Supabase stores only non-secret metadata such as `remote_device_id`, port, and version.

On the host, open Settings -> Remote Control and set a Remote Command password. This password authorizes command execution only: shutdown, restart, lock, and saved custom actions. It is not a RustDesk password and does not replace RustDesk unattended access.

On the controller, every command opens a small password prompt. The controller exchanges that password over Tailscale for a temporary host token, runs that one command, then discards the token. Remote commands include `Authorization: Bearer <token>` and the host validates it before doing anything.

The password itself is not stored in plaintext. The host stores a salted PBKDF2-SHA256 verifier inside the DPAPI-protected credentials file. The password exchange uses plain HTTP over Tailscale, so only use this on your private tailnet; do not expose the host port to the public internet.

## PCs, Health, Wake, And RustDesk

The main cards are data-driven from `public.pc_remote_control`. A card shows whether the PC is offline, reachable, or whether the RemotePC tray host is ready. `Connect` still works without host mode: it checks Tailscale/RustDesk status, calls Supabase `wake_pc` if needed, waits for reachability, and opens RustDesk.

RustDesk integration stores only `rustdesk_id`; authentication stays inside RustDesk. Wake-on-LAN remains Supabase -> ESP32-S3 -> PC and is not replaced by the host listener.

## Custom Actions

Run `Migrations/002_host_mode_and_actions.sql` after the existing migration. It adds host metadata columns and creates `public.pc_commands`.

Actions are saved per machine. The controller sends only the action id to the host. The host reloads the saved action from Supabase, confirms it belongs to the local PC row, checks `enabled`, and then executes it.

Supported action types:

- `powershell`: runs the saved command through `pwsh.exe` if available, otherwise `powershell.exe`, with no profile, redirected stdout/stderr, cancellation, timeout, exit code, duration, and output caps.
- `process`: launches the saved executable path with saved arguments/working directory from the logged-in user session, so GUI apps can appear normally.

RemotePC also applies a local safety policy in the action editor and again on the host before execution. It blocks obvious system-destructive patterns such as disk formatting tools, boot configuration tools, registry/system permission tools, encoded PowerShell, broad recursive delete commands, and deletes aimed at Windows/user/system roots. This is a guardrail, not a sandbox: only save narrow actions you understand.

Built-in Shutdown and Restart are not stored as PowerShell. They call `shutdown.exe` directly and require confirmation from the controller. Lock uses the Windows `LockWorkStation` API and is immediate.

## Supabase Security

`pc_commands` has RLS enabled. Because this app currently uses a publishable key without Supabase sign-in, the migration grants `anon` access to manage saved action definitions. This is practical for a trusted personal project, but not production multi-user security. Anyone with the Supabase URL and publishable key could read or change action definitions, although they still cannot execute commands on a host without the Remote Command password/token and the host-side safety checks.

Raw host tokens, pairing codes, private keys, and RustDesk passwords are never stored in Supabase.

## Firewall

The app does not require administrator privileges during normal use. If Windows Firewall blocks the host port, run PowerShell as Administrator once:

```powershell
.\setup\Configure-RemotePCFirewall.ps1 -Port 47632
```

The rule allows TCP inbound on Private networks from Tailscale CGNAT addresses `100.64.0.0/10`. Remove it with:

```powershell
.\setup\Remove-RemotePCFirewall.ps1
```

## Build

```powershell
dotnet restore
dotnet build
```

## Publish

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

Single-file publish is optional; reliability is more important than forcing a one-file output.

## Manual Setup Still Required

- Run `Migrations/001_extend_pc_remote_control.sql`.
- Run `Migrations/002_host_mode_and_actions.sql`.
- Keep Tailscale installed and logged in on each PC.
- Keep RustDesk installed/configured for unattended access and login-screen access.
- Enable host mode in RemotePC Settings on machines that should receive commands.
- Set a Remote Command password on each host, then authorize each controller from the Advanced page.
- Add owner-based Supabase Auth/RLS before production or shared use of `pc_commands`.
