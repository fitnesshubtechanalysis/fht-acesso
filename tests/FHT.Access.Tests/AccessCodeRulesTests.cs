using FHT.Access.Application.Services;
using FHT.Access.Domain;

namespace FHT.Access.Tests;

public class AccessCodeRulesTests
{
    [Fact]
    public void Keeps_four_and_six_digit_codes_and_drops_the_rest()
    {
        var codes = AccessCodeRules.Normalize(["4821", "482199", "12", "48215", "ab4821", "  123456 "]);
        Assert.Equal(["4821", "482199", "123456"], codes);
    }

    [Fact]
    public void Four_digits_wait_and_six_digits_submit_immediately()
    {
        var step = new KeypadBuffer.Step(string.Empty, null, false);
        foreach (var digit in new[] { "4", "8", "2", "1" })
            step = KeypadBuffer.Apply(step.Buffer, digit, bufferExpired: false);

        Assert.Equal("4821", step.Buffer);
        Assert.Null(step.SubmitNow);
        Assert.True(step.WaitForMoreDigits);

        step = KeypadBuffer.Apply(step.Buffer, "9", bufferExpired: false);
        Assert.Equal("48219", step.Buffer);
        Assert.Null(step.SubmitNow);
        Assert.False(step.WaitForMoreDigits);

        step = KeypadBuffer.Apply(step.Buffer, "9", bufferExpired: false);
        Assert.Equal(string.Empty, step.Buffer);
        Assert.Equal("482199", step.SubmitNow);
        Assert.False(step.WaitForMoreDigits);
    }

    [Fact]
    public void A_complete_four_digit_packet_submits_without_waiting()
    {
        var step = KeypadBuffer.Apply(string.Empty, "4821", bufferExpired: false);
        Assert.Equal("4821", step.SubmitNow);
        Assert.False(step.WaitForMoreDigits);
    }

    [Fact]
    public void Enter_confirms_whatever_was_typed()
    {
        var step = KeypadBuffer.Confirm("4821");
        Assert.Equal("4821", step.SubmitNow);
        Assert.Equal(string.Empty, step.Buffer);
    }
}
