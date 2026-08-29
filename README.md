# RemotePC

RemotePC is a small Avalonia desktop utility for waking and connecting to home PCs through a Supabase-driven command table. It loads PCs from Supabase, checks each Tailscale IP concurrently, sends an atomic wake command when needed, waits for the PC to become reachable, and then opens RustDesk.

## Requirements

- .NET SDK 9 on this machine, targeting `net9.0`
- A Supabase project with `public.pc_remote_control`
- Tailscale installed/configured on each managed PC
- RustDesk installed on the client machine

## Supabase Configuration

Open the app's gear button to edit settings, or copy `appsettings.example.json` to `appsettings.json` and set:

```json
{
  "Supabase": {
    "Url": "https://YOUR_PROJECT.supabase.co",
    "PublishableKey": "YOUR_PUBLISHABLE_KEY"
  }
}
```

Use a Supabase publishable key only. Do not use `service_role` or any secret key in this desktop app. `appsettings.json` is ignored by Git and copied to the build output.

## SQL Migration

Run [Migrations/001_extend_pc_remote_control.sql](Migrations/001_extend_pc_remote_control.sql) in the Supabase SQL editor. It adds the app fields with `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`, safely renames or merges `parsec_peer_id` into `rustdesk_id`, enables RLS, grants read access to rows, and creates the `wake_pc(target_id bigint)`, `add_pc_device(...)`, `update_pc_device(...)`, and `delete_pc_device(...)` RPC functions.

## Adding Another PC

Use the plus button in the app to add a PC. Use each card's edit button to update its name, Tailscale IP, RustDesk ID, or enabled state. Use each card's delete button to remove its Supabase row after confirmation. The app uses RPCs for these changes and keeps direct table writes closed.

The app is data-driven; PCs added through the app or directly in Supabase appear without C# changes. Rows with `enabled = false` still appear, but show as disabled and cannot be connected.

## ESP32 Integration

The app never sends Wake-on-LAN directly. For an offline PC, it calls Supabase RPC `wake_pc`, which increments `command_id`. Your ESP32 keeps polling its row and sends Wake-on-LAN when `command_id` is greater than the last command it observed.

## Tailscale

Online detection uses the `tailscale_ip` stored in Supabase, not a home LAN address. The app also asks the configured RustDesk rendezvous server whether the row's `rustdesk_id` is online. If either Tailscale or RustDesk reports the PC online, RemotePC treats it as reachable.

## RustDesk

Install RustDesk on the home PC. If unattended startup is required, install RustDesk rather than only using it as a temporary portable app. Configure unattended access and the permanent password inside RustDesk itself.

Never put the RustDesk password in Supabase, `appsettings.json`, or source code. RustDesk handles saved unattended credentials.

Find the home PC's RustDesk ID and store only that ID in `public.pc_remote_control.rustdesk_id`. Install RustDesk on the laptop/client machine too, then test a connection while the home PC is at the Windows lock screen.

RemotePC looks for RustDesk in common Windows locations under `C:\Program Files\RustDesk`, `C:\Program Files (x86)\RustDesk`, `%LOCALAPPDATA%\Programs\RustDesk`, `%LOCALAPPDATA%\RustDesk`, and the current process `PATH`. It prefers RustDesk's `rustdesk://connection/new/<rustdesk_id>` URI launch for the selected Supabase row, then falls back to `rustdesk.exe --connect <rustdesk_id>` if the URI handler is unavailable.

If a Supabase row matches the laptop's own RustDesk ID or local Tailscale IP, RemotePC marks it as `This PC` and disables Connect to prevent self-connections.

## Architecture

```text
Laptop
  |
Avalonia RemotePC
  |
check Tailscale IP
  |
check RustDesk online status
  |
if offline
  |
Supabase
  |
ESP32-S3
  |
Wake-on-LAN
  |
Windows / RustDesk service starts
  |
Tailscale becomes reachable
  |
Avalonia launches RustDesk
  |
remote session
```

Tailscale is used by this application for online detection. RustDesk handles the actual remote-desktop connection.

## Run And Build

```powershell
dotnet restore
dotnet build
dotnet run
```

## Publish For Windows

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The publish command enables single-file output and native library self-extraction to keep Avalonia startup reliable.
