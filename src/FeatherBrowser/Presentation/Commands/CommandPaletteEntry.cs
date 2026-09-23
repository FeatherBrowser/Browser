namespace FeatherBrowser.Presentation.Commands;

internal sealed class CommandPaletteEntry
{
    public string Label { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Kind { get; set; } = "command";
    public string Data { get; set; } = "";
    public Guid? TabId { get; set; }
    public override string ToString() => string.IsNullOrWhiteSpace(Detail) ? Label : $"{Label}   ·   {Detail}";
}
