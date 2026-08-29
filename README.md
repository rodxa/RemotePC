# RemotePC

RemotePC is a small Avalonia desktop utility for waking and connecting to home PCs through a Supabase-driven command table. It loads enabled PCs from Supabase, checks each Tailscale IP concurrently, sends an atomic wake command when needed, waits for the PC to become reachable, and then opens Parsec.

## Requirements

- .NET SDK 9 on this machine, targeting `net9.0`
- A Supabase project with `public.pc_remote_control`
- Tailscale installed/configured on each managed PC
- Parsec installed on the client machine

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

Run [Migrations/001_extend_pc_remote_control.sql](Migrations/001_extend_pc_remote_control.sql) in the Supabase SQL editor. It adds the app fields with `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`, enables RLS, grants read access to enabled rows, and creates the `wake_pc(target_id bigint)` RPC function that atomically increments `command_id`.

## Adding Another PC

Insert a new row in `public.pc_remote_control` with a unique `device_name`, optional `display_name`, the PC's `tailscale_ip`, optional `parsec_peer_id`, `enabled = true`, and a `sort_order`. The app is data-driven; refresh or restart and the new PC appears without C# changes.

## ESP32 Integration

The app never sends Wake-on-LAN directly. For an offline PC, it calls Supabase RPC `wake_pc`, which increments `command_id`. Your ESP32 keeps polling its row and sends Wake-on-LAN when `command_id` is greater than the last command it observed.

## Tailscale

Online detection uses the `tailscale_ip` stored in Supabase, not a home LAN address. The app uses short asynchronous ICMP pings and checks multiple PCs concurrently.

## Parsec

RemotePC looks for Parsec in common Windows locations under `%LOCALAPPDATA%\Parsec`, `%LOCALAPPDATA%\Programs\Parsec`, `C:\Program Files\Parsec`, and `C:\Program Files (x86)\Parsec`. It opens Parsec normally. `parsec_peer_id` is modeled for future direct-launch support, but no undocumented command-line parameters are invented.

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
