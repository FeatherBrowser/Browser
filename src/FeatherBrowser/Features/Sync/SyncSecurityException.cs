namespace FeatherBrowser.Features.Sync;

internal sealed class SyncSecurityException : Exception
{
    public SyncSecurityException(string message) : base(message)
    {
    }

    public SyncSecurityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
