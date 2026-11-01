using System.Text.Json.Serialization;

namespace TheMercenaryServer.Compat;

public sealed class CompatRuleSet
{
    [JsonPropertyName("rules")]
    public List<CompatRule> Rules { get; init; } = [];
}

public sealed class CompatRule
{
    [JsonPropertyName("TARGET-TPL")]
    public string TargetTpl { get; init; } = string.Empty;

    [JsonPropertyName("ALLOWED_TPLS")]
    public List<string> AllowedTpls { get; init; } = [];

    [JsonPropertyName("SLOT_NAMES")]
    public List<string>? SlotNames { get; init; }

    [JsonPropertyName("REPLACE")]
    public bool Replace { get; init; }
}

public sealed class CompatReport
{
    public int Files { get; set; }
    public int Rules { get; set; }
    public int TargetsFound { get; set; }
    public int TargetsMissing { get; set; }
    public int FiltersTouched { get; set; }
    public int Added { get; set; }
    public int Replaced { get; set; }
    public int MissingAllowedTemplates { get; set; }
}
