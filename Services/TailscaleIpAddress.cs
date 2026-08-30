namespace RemotePC.Services;

public static class TailscaleIpAddress
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var slashIndex = trimmed.IndexOf('/', StringComparison.Ordinal);
        return slashIndex > 0 ? trimmed[..slashIndex] : trimmed;
    }
}
