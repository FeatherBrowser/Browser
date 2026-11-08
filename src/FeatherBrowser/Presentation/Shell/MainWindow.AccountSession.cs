using FeatherBrowser.Infrastructure.Supabase;

namespace FeatherBrowser.Presentation.Shell;

public partial class MainWindow
{
    private int _accountGeneration;

    private void BeginAccountSession(SupabaseSession? session)
    {
        _accountGeneration++;
        _accountSession = session;
    }

    private async Task<SupabaseSession?> RefreshAccountSessionIfNeededAsync()
    {
        SupabaseSession? current = _accountSession;
        if (current is null || !current.NeedsRefresh)
            return current;

        int generation = _accountGeneration;
        SupabaseSession refreshed = await _supabaseAuth.RefreshAsync(current.RefreshToken);

        if (!ReferenceEquals(_accountSession, current))
            return generation == _accountGeneration ? _accountSession : null;

        return AdoptAccountSession(refreshed, generation, current.Email) ? _accountSession : null;
    }

    private bool AdoptAccountSession(SupabaseSession session, int generation, string? fallbackEmail)
    {
        if (generation != _accountGeneration || _accountSession is null)
            return false;

        if (string.IsNullOrWhiteSpace(session.Email) && !string.IsNullOrWhiteSpace(fallbackEmail))
        {
            session = new SupabaseSession
            {
                UserId = session.UserId,
                AccessToken = session.AccessToken,
                RefreshToken = session.RefreshToken,
                Email = fallbackEmail,
                ExpiresAt = session.ExpiresAt
            };
        }

        _accountSession = session;
        _supabaseSessionStore.Save(session);
        return true;
    }
}
