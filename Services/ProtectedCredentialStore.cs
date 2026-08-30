using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RemotePC.Services;

public sealed class ProtectedCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RemotePC.v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public ProtectedCredentialStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RemotePC");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "credentials.json");
    }

    public string GetOrCreateLocalDeviceId()
    {
        var credentials = Load();
        if (!string.IsNullOrWhiteSpace(credentials.LocalDeviceId))
        {
            return credentials.LocalDeviceId;
        }

        credentials.LocalDeviceId = Guid.NewGuid().ToString("D");
        Save(credentials);
        return credentials.LocalDeviceId;
    }

    public string GetOrCreateHostToken()
    {
        var credentials = Load();
        if (!string.IsNullOrWhiteSpace(credentials.HostToken))
        {
            return credentials.HostToken;
        }

        credentials.HostToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Save(credentials);
        return credentials.HostToken;
    }

    public string? GetHostTokenForPc(long pcId)
    {
        var key = pcId.ToString();
        var credentials = Load();
        return credentials.PairedHosts.TryGetValue(key, out var token) ? token : null;
    }

    public void SaveHostTokenForPc(long pcId, string token)
    {
        var credentials = Load();
        credentials.PairedHosts[pcId.ToString()] = token;
        Save(credentials);
    }

    private Credentials Load()
    {
        if (!File.Exists(_path))
        {
            return new Credentials();
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(File.ReadAllText(_path));
            var bytes = OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser)
                : protectedBytes;
            return JsonSerializer.Deserialize<Credentials>(bytes) ?? new Credentials();
        }
        catch
        {
            return new Credentials();
        }
    }

    private void Save(Credentials credentials)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credentials, JsonOptions);
        var protectedBytes = OperatingSystem.IsWindows()
            ? ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)
            : bytes;
        File.WriteAllText(_path, Convert.ToBase64String(protectedBytes));
    }

    private sealed class Credentials
    {
        public string? LocalDeviceId { get; set; }

        public string? HostToken { get; set; }

        public Dictionary<string, string> PairedHosts { get; set; } = new();
    }
}
