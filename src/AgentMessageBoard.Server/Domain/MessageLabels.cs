namespace AgentMessageBoard.Server.Domain;

/// <summary>
/// The wire names of <see cref="MessageLabel"/> (snake_case). The enum's JSON attributes, validation messages, tool
/// descriptions and the REST <c>label</c> parser all draw on these constants, so a name is spelled out exactly once.
/// </summary>
public static class MessageLabels
{
    public const string Activity = "activity";
    public const string HelpRequired = "help_required";
    public const string Fix = "fix";

    /// <summary>Every wire name, comma separated, for validation messages: "activity, help_required, fix".</summary>
    public const string All = $"{Activity}, {HelpRequired}, {Fix}";

    private static readonly MessageLabel[] Values = Enum.GetValues<MessageLabel>();

    public static string ToWireName(this MessageLabel label) => label switch
    {
        MessageLabel.Activity => Activity,
        MessageLabel.HelpRequired => HelpRequired,
        MessageLabel.Fix => Fix,
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Not a defined MessageLabel."),
    };

    /// <summary>Lenient parse for query strings: wire names or enum member names, any casing, '-' accepted for '_'.</summary>
    public static bool TryParse(string? value, out MessageLabel label)
    {
        var normalized = value?.Trim().Replace('-', '_');
        foreach (var candidate in Values)
        {
            if (Matches(normalized, candidate.ToWireName()) || Matches(normalized, candidate.ToString()))
            {
                label = candidate;
                return true;
            }
        }

        label = default;
        return false;
    }

    private static bool Matches(string? value, string name) => string.Equals(value, name, StringComparison.OrdinalIgnoreCase);
}
