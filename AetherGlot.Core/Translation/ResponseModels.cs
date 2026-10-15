using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherGlot.Core.Translation;

/// <summary>A term the model translated (or was told to use).</summary>
public sealed class TermInfo
{
    [JsonPropertyName("src")]   public string Src { get; set; } = string.Empty;
    [JsonPropertyName("dst")]   public string Dst { get; set; } = string.Empty;

    /// <summary>"dict" | "slang" | "llm".</summary>
    [JsonPropertyName("from")]  public string? From { get; set; }
    [JsonPropertyName("sense")] public string? Sense { get; set; }
    [JsonPropertyName("conf")]  public double? Conf { get; set; }
    [JsonPropertyName("alts")]  public List<string> Alts { get; set; } = [];
}

/// <summary>A span the model could not translate; it stays in the original language.</summary>
public sealed class UnresolvedSpan
{
    [JsonPropertyName("id")]    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("text")]  public string Text { get; set; } = string.Empty;
    [JsonPropertyName("guess")] public string? Guess { get; set; }
}

/// <summary>Inbound response schema (see README / design spec).</summary>
public sealed class InboundResponse
{
    public const string StatusOk      = "ok";
    public const string StatusPartial = "partial";
    public const string StatusSkip    = "skip";

    [JsonPropertyName("status")]      public string Status { get; set; } = string.Empty;
    [JsonPropertyName("lang")]        public string Lang { get; set; } = string.Empty;
    [JsonPropertyName("translation")] public string? Translation { get; set; }
    [JsonPropertyName("terms")]       public List<TermInfo> Terms { get; set; } = [];
    [JsonPropertyName("unresolved")]  public List<UnresolvedSpan> Unresolved { get; set; } = [];

    /// <summary>Reserved for sense decisions; kept raw.</summary>
    [JsonPropertyName("senses")]      public List<JsonElement> Senses { get; set; } = [];
}

/// <summary>Outbound (send) response schema.</summary>
public sealed class OutboundResponse
{
    [JsonPropertyName("translation")]      public string? Translation { get; set; }
    [JsonPropertyName("back_translation")] public string? BackTranslation { get; set; }
    [JsonPropertyName("alternatives")]     public List<string> Alternatives { get; set; } = [];
    [JsonPropertyName("terms")]            public List<TermInfo> Terms { get; set; } = [];
}
