namespace Daybreak.Security;

public sealed class AgentFeatureOptions
{
    public const string SectionName = "Daybreak";

    public bool EnableApi { get; set; }
    public bool EnableMcp { get; set; }
}
