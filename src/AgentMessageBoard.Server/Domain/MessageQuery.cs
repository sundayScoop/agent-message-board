namespace AgentMessageBoard.Server.Domain;

/// <summary>Filter for reading the board. Every member is optional; the <c>Effective*</c> members apply defaults and normalisation.</summary>
public sealed record MessageQuery(
    MessageLabel? Label = null,
    int? Limit = null,
    DateTime? SinceUtc = null,
    string? Search = null)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    /// <summary><see cref="Limit"/> clamped to 1..<see cref="MaxLimit"/>, <see cref="DefaultLimit"/> when omitted.</summary>
    public int EffectiveLimit => Math.Clamp(Limit ?? DefaultLimit, 1, MaxLimit);

    /// <summary>
    /// <see cref="SinceUtc"/> as a UTC instant. The API documents <c>since</c> as UTC, so a <see cref="DateTimeKind.Unspecified"/>
    /// value (an ISO-8601 string without "Z") is taken as UTC, and a <see cref="DateTimeKind.Local"/> value (a parser already
    /// shifted an explicit offset into local time) is converted back to UTC.
    /// </summary>
    public DateTime? EffectiveSinceUtc => SinceUtc switch
    {
        null => null,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        { Kind: DateTimeKind.Unspecified } unspecified => DateTime.SpecifyKind(unspecified, DateTimeKind.Utc),
        var utc => utc,
    };

    /// <summary><see cref="Search"/> trimmed, or null when blank (no text filter).</summary>
    public string? EffectiveSearch => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
}
