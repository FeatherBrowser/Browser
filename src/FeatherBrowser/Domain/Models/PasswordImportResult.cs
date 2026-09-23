namespace FeatherBrowser.Domain.Models;

internal sealed class PasswordImportResult
{
    public int Imported { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
}
