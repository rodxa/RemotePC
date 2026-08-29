using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace RemotePC.Services;

public sealed class RustDeskService
{
    private const string ExecutableName = "rustdesk.exe";
    private const string RustDeskUriScheme = "rustdesk";
    private const string DefaultRendezvousServer = "rs-ny.rustdesk.com:21116";
    private const int DefaultRendezvousPort = 21116;
    private static readonly TimeSpan OnlineCheckTimeout = TimeSpan.FromSeconds(4);
    private static readonly Regex RendezvousServerRegex = new(
        @"^\s*rendezvous_server\s*=\s*['""](?<server>[^'""]+)['""]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task LaunchAsync(string? rustDeskId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedRustDeskId = NormalizeRustDeskId(rustDeskId);
        if (string.IsNullOrWhiteSpace(normalizedRustDeskId))
        {
            throw new InvalidOperationException("RustDesk ID not configured");
        }

        var executable = FindRustDeskExecutable();
        if (executable is null)
        {
            throw new FileNotFoundException("RustDesk is not installed. Install RustDesk or check its installation path.");
        }

        if (TryLaunchRustDeskUri(normalizedRustDeskId))
        {
            return Task.CompletedTask;
        }

        var startInfo = CreateStartInfo(executable);
        startInfo.ArgumentList.Add("--connect");
        startInfo.ArgumentList.Add(normalizedRustDeskId);

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("RustDesk could not be started.");
        }

        return Task.CompletedTask;
    }

    public async Task<bool?> IsPeerOnlineAsync(string? rustDeskId, CancellationToken cancellationToken)
    {
        var normalizedRustDeskId = NormalizeRustDeskId(rustDeskId);
        if (string.IsNullOrWhiteSpace(normalizedRustDeskId))
        {
            return false;
        }

        var executable = FindRustDeskExecutable();
        if (executable is null)
        {
            return null;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(OnlineCheckTimeout);

        try
        {
            var target = ParseRustDeskTarget(normalizedRustDeskId);
            var localId = await GetLocalRustDeskIdAsync(executable, timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(localId))
            {
                return null;
            }

            var rendezvousServer = target.RendezvousServer ?? GetConfiguredRendezvousServer();
            var (host, port) = ParseServerEndpoint(rendezvousServer);
            var onlinePort = port - 1;
            if (onlinePort <= 0)
            {
                return null;
            }

            using var client = new TcpClient();
            await client.ConnectAsync(host, onlinePort, timeoutCts.Token);

            await using var stream = client.GetStream();
            var request = CreateOnlineRequest(localId.Trim(), target.PeerId);
            await stream.WriteAsync(request, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);

            var response = await ReadFrameAsync(stream, timeoutCts.Token);
            return response is not null && ParseOnlineResponse(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or ArgumentException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public async Task<bool?> IsLocalRustDeskIdAsync(string? rustDeskId, CancellationToken cancellationToken)
    {
        var normalizedRustDeskId = NormalizeRustDeskId(rustDeskId);
        if (string.IsNullOrWhiteSpace(normalizedRustDeskId))
        {
            return false;
        }

        var executable = FindRustDeskExecutable();
        if (executable is null)
        {
            return null;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(OnlineCheckTimeout);

        try
        {
            var target = ParseRustDeskTarget(normalizedRustDeskId);
            var localId = await GetLocalRustDeskIdAsync(executable, timeoutCts.Token);
            return string.Equals(
                NormalizeRustDeskId(localId),
                NormalizeRustDeskId(target.PeerId),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public static string NormalizeRustDeskId(string? rustDeskId)
    {
        if (string.IsNullOrWhiteSpace(rustDeskId))
        {
            return string.Empty;
        }

        return string.Concat(rustDeskId.Where(static c => !char.IsWhiteSpace(c)));
    }

    private static async Task<string?> GetLocalRustDeskIdAsync(string executable, CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(executable);
        startInfo.ArgumentList.Add("--get-id");
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .FirstOrDefault(static line => line.Length > 0);
    }

    private static RustDeskTarget ParseRustDeskTarget(string rustDeskId)
    {
        var idPart = rustDeskId;
        string? server = null;

        var atIndex = rustDeskId.IndexOf('@', StringComparison.Ordinal);
        if (atIndex > 0 && atIndex < rustDeskId.Length - 1)
        {
            idPart = rustDeskId[..atIndex];
            server = rustDeskId[(atIndex + 1)..];
            var slashIndex = server.IndexOf('/', StringComparison.Ordinal);
            if (slashIndex >= 0)
            {
                server = server[..slashIndex];
            }
        }

        return new RustDeskTarget(idPart, string.IsNullOrWhiteSpace(server) ? null : server);
    }

    private static string GetConfiguredRendezvousServer()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            var path = Path.Combine(appData, "RustDesk", "config", "RustDesk2.toml");
            if (File.Exists(path))
            {
                foreach (var line in File.ReadLines(path))
                {
                    var match = RendezvousServerRegex.Match(line);
                    if (match.Success)
                    {
                        return match.Groups["server"].Value;
                    }
                }
            }
        }

        return DefaultRendezvousServer;
    }

    private static (string Host, int Port) ParseServerEndpoint(string server)
    {
        if (Uri.TryCreate($"tcp://{server}", UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return (uri.Host, uri.Port > 0 ? uri.Port : DefaultRendezvousPort);
        }

        return (server, DefaultRendezvousPort);
    }

    private static byte[] CreateOnlineRequest(string localId, string peerId)
    {
        using var onlineRequest = new MemoryStream();
        WriteStringField(onlineRequest, 1, localId);
        WriteStringField(onlineRequest, 2, peerId);

        using var rendezvousMessage = new MemoryStream();
        WriteLengthDelimitedField(rendezvousMessage, 23, onlineRequest.ToArray());

        return WriteFrame(rendezvousMessage.ToArray());
    }

    private static bool ParseOnlineResponse(byte[] rendezvousMessage)
    {
        var index = 0;
        while (TryReadField(rendezvousMessage, ref index, out var fieldNumber, out var wireType))
        {
            if (fieldNumber == 24 && wireType == 2)
            {
                var onlineResponse = ReadLengthDelimited(rendezvousMessage, ref index);
                return ParseOnlineResponsePayload(onlineResponse);
            }

            SkipField(rendezvousMessage, ref index, wireType);
        }

        return false;
    }

    private static bool ParseOnlineResponsePayload(byte[] onlineResponse)
    {
        var index = 0;
        while (TryReadField(onlineResponse, ref index, out var fieldNumber, out var wireType))
        {
            if (fieldNumber == 1 && wireType == 2)
            {
                var states = ReadLengthDelimited(onlineResponse, ref index);
                return states.Length > 0 && (states[0] & 0x80) == 0x80;
            }

            SkipField(onlineResponse, ref index, wireType);
        }

        return false;
    }

    private static async Task<byte[]?> ReadFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var first = await ReadExactAsync(stream, 1, cancellationToken);
        if (first is null || first.Length == 0)
        {
            return null;
        }

        var headerLength = (first[0] & 0x03) + 1;
        var header = new byte[headerLength];
        header[0] = first[0];

        if (headerLength > 1)
        {
            var rest = await ReadExactAsync(stream, headerLength - 1, cancellationToken);
            if (rest is null)
            {
                return null;
            }

            rest.CopyTo(header, 1);
        }

        var length = 0;
        for (var i = 0; i < header.Length; i++)
        {
            length |= header[i] << (8 * i);
        }

        length >>= 2;
        return await ReadExactAsync(stream, length, cancellationToken);
    }

    private static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
            {
                return null;
            }

            offset += read;
        }

        return buffer;
    }

    private static byte[] WriteFrame(byte[] payload)
    {
        using var stream = new MemoryStream();
        if (payload.Length <= 0x3F)
        {
            stream.WriteByte((byte)(payload.Length << 2));
        }
        else if (payload.Length <= 0x3FFF)
        {
            WriteLittleEndianHeader(stream, (payload.Length << 2) | 0x01, 2);
        }
        else if (payload.Length <= 0x3FFFFF)
        {
            WriteLittleEndianHeader(stream, (payload.Length << 2) | 0x02, 3);
        }
        else
        {
            WriteLittleEndianHeader(stream, (payload.Length << 2) | 0x03, 4);
        }

        stream.Write(payload);
        return stream.ToArray();
    }

    private static void WriteLittleEndianHeader(Stream stream, int value, int length)
    {
        for (var i = 0; i < length; i++)
        {
            stream.WriteByte((byte)(value >> (8 * i)));
        }
    }

    private static void WriteStringField(Stream stream, int fieldNumber, string value)
    {
        WriteLengthDelimitedField(stream, fieldNumber, Encoding.UTF8.GetBytes(value));
    }

    private static void WriteLengthDelimitedField(Stream stream, int fieldNumber, byte[] value)
    {
        WriteVarint(stream, (uint)((fieldNumber << 3) | 2));
        WriteVarint(stream, (uint)value.Length);
        stream.Write(value);
    }

    private static bool TryReadField(byte[] payload, ref int index, out int fieldNumber, out int wireType)
    {
        fieldNumber = 0;
        wireType = 0;
        if (index >= payload.Length || !TryReadVarint(payload, ref index, out var key))
        {
            return false;
        }

        fieldNumber = (int)(key >> 3);
        wireType = (int)(key & 0x07);
        return true;
    }

    private static byte[] ReadLengthDelimited(byte[] payload, ref int index)
    {
        if (!TryReadVarint(payload, ref index, out var length) || length > int.MaxValue || index + (int)length > payload.Length)
        {
            return [];
        }

        var value = payload[index..(index + (int)length)];
        index += (int)length;
        return value;
    }

    private static void SkipField(byte[] payload, ref int index, int wireType)
    {
        switch (wireType)
        {
            case 0:
                TryReadVarint(payload, ref index, out _);
                break;
            case 1:
                index = Math.Min(index + 8, payload.Length);
                break;
            case 2:
                _ = ReadLengthDelimited(payload, ref index);
                break;
            case 5:
                index = Math.Min(index + 4, payload.Length);
                break;
            default:
                index = payload.Length;
                break;
        }
    }

    private static void WriteVarint(Stream stream, uint value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }

    private static bool TryReadVarint(byte[] payload, ref int index, out uint value)
    {
        value = 0;
        var shift = 0;
        while (index < payload.Length && shift < 32)
        {
            var current = payload[index++];
            value |= (uint)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        return false;
    }

    private static bool TryLaunchRustDeskUri(string rustDeskId)
    {
        if (!IsRustDeskUriSchemeRegistered())
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = $"rustdesk://connection/new/{Uri.EscapeDataString(rustDeskId)}",
                UseShellExecute = true
            };

            return Process.Start(startInfo) is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool IsRustDeskUriSchemeRegistered()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{RustDeskUriScheme}\shell\open\command");
            return key?.GetValue(null) is string command && !string.IsNullOrWhiteSpace(command);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executable)
    {
        return new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
        };
    }

    private static string? FindRustDeskExecutable()
    {
        foreach (var directory in GetCandidateDirectories())
        {
            var path = Path.Combine(directory, ExecutableName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateDirectories()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "RustDesk");
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "RustDesk");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "RustDesk");
            yield return Path.Combine(localAppData, "RustDesk");
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            yield break;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return directory;
        }
    }

    private readonly record struct RustDeskTarget(string PeerId, string? RendezvousServer);
}
