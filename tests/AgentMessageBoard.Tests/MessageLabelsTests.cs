using System.Text.Json;
using AgentMessageBoard.Server.Domain;

namespace AgentMessageBoard.Tests;

/// <summary>The wire vocabulary of labels: JSON form, validation text and the lenient REST parser agree with each other.</summary>
public sealed class MessageLabelsTests
{
    public static TheoryData<MessageLabel> AllLabels => new(Enum.GetValues<MessageLabel>());

    [Theory]
    [MemberData(nameof(AllLabels))]
    public void Wire_name_is_the_json_form_and_round_trips_through_TryParse(MessageLabel label)
    {
        var wireName = label.ToWireName();

        Assert.Equal($"\"{wireName}\"", JsonSerializer.Serialize(label));
        Assert.Equal(label, JsonSerializer.Deserialize<MessageLabel>($"\"{wireName}\""));
        Assert.True(MessageLabels.TryParse(wireName, out var parsed));
        Assert.Equal(label, parsed);
    }

    [Fact]
    public void All_lists_every_label_in_declaration_order()
    {
        Assert.Equal("activity, help_required, fix", MessageLabels.All);
        Assert.Equal(MessageLabels.All, string.Join(", ", Enum.GetValues<MessageLabel>().Select(l => l.ToWireName())));
    }

    [Theory]
    [InlineData("fix", MessageLabel.Fix)]
    [InlineData(" FIX ", MessageLabel.Fix)]
    [InlineData("help_required", MessageLabel.HelpRequired)]
    [InlineData("help-required", MessageLabel.HelpRequired)]
    [InlineData("HelpRequired", MessageLabel.HelpRequired)]
    [InlineData("helprequired", MessageLabel.HelpRequired)]
    [InlineData("Activity", MessageLabel.Activity)]
    public void TryParse_is_lenient_about_case_separators_and_whitespace(string value, MessageLabel expected)
    {
        Assert.True(MessageLabels.TryParse(value, out var label));
        Assert.Equal(expected, label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("urgent")]
    [InlineData("fixes")]
    [InlineData("help required")]
    [InlineData("0")]
    public void TryParse_rejects_anything_else(string? value)
    {
        Assert.False(MessageLabels.TryParse(value, out _));
    }

    [Fact]
    public void ToWireName_rejects_undefined_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((MessageLabel)42).ToWireName());
    }
}
