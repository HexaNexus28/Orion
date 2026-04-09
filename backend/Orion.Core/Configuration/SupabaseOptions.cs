namespace Orion.Core.Configuration;

public class SupabaseOptions
{
    public const string SectionName = "Supabase";
    
    public string ConnectionString { get; set; } = string.Empty;
}
