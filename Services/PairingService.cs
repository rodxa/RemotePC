using System.Security.Cryptography;
using RemotePC.Models;

namespace RemotePC.Services;

public sealed class PairingService
{
    private readonly object _sync = new();
    private PairingCodeInfo? _current;

    public PairingCodeInfo CreatePairingCode(TimeSpan lifetime)
    {
        var number = RandomNumberGenerator.GetInt32(100000, 1000000);
        var info = new PairingCodeInfo
        {
            Code = number.ToString("D6"),
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime)
        };

        lock (_sync)
        {
            _current = info;
        }

        return info;
    }

    public bool TryConsume(string code)
    {
        lock (_sync)
        {
            if (_current is null ||
                _current.ExpiresAt <= DateTimeOffset.UtcNow ||
                !string.Equals(_current.Code, code.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            _current = null;
            return true;
        }
    }
}
