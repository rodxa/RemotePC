namespace RemotePC.Services;

public sealed class SupabaseException : Exception
{
    public SupabaseException(string message)
        : base(message)
    {
    }

    public SupabaseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
