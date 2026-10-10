namespace GamingHub.Core.Models;

public sealed class Badge
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Glyph { get; set; } = "\uE7FC";
    public bool Earned { get; set; }
    public DateTime? EarnedUtc { get; set; }
}
