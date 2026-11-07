namespace FeatherBrowser.Infrastructure.Supabase;

internal static class FeatherSupabase
{
    private const string ProjectUrl =
        "https://anzqlqynknipyjdsehrs.supabase.co";

    private const string PublishableKey =
        "sb_publishable_FW5VuW_zj2rdR8TdoQLXlQ_OobkiPxu";

    public static SupabaseConfig CreateConfig() =>
        new(ProjectUrl, PublishableKey);
}