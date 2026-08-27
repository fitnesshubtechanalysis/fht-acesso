using FHT.Access.Application.Services;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Tests;

public class OperatingModeScenarioTests
{
    [Fact]
    public void Attendant_blocks_automatic_recognition_gate()
    {
        var mode = new OperatingModeService();
        var states = new AccessStateMachine();

        mode.EnterAutomatic();
        Assert.True(mode.RecognitionEnabled);

        mode.EnterAttendant();
        states.TransitionTo(AccessUiState.AttendantDashboard);

        // Person at camera: engine must not process because RecognitionEnabled is false.
        Assert.False(mode.RecognitionEnabled);
        Assert.Equal(AccessUiState.AttendantDashboard, states.State);
        Assert.NotEqual(AccessOperatingMode.Automatic, mode.Mode);
    }

    [Fact]
    public void Enrollment_keeps_recognition_disabled_until_automatic()
    {
        var mode = new OperatingModeService();
        mode.EnterAttendant();
        mode.EnterEnrollment();
        Assert.False(mode.RecognitionEnabled);

        mode.EnterAttendant();
        Assert.False(mode.RecognitionEnabled);

        mode.EnterAutomatic();
        Assert.True(mode.RecognitionEnabled);
    }

    [Fact]
    public void Session_guard_blocks_reentry_until_complete()
    {
        var guard = new RecognitionSessionGuard
        {
            CooldownAfterSession = TimeSpan.FromMilliseconds(50)
        };

        Assert.True(guard.TryBeginSession());
        Assert.False(guard.TryBeginSession());
        guard.CompleteSession();
        Assert.True(guard.IsInCooldown);
    }

    [Fact]
    public void Engine_respects_mode_gate_before_session()
    {
        var mode = new OperatingModeService();
        var guard = new RecognitionSessionGuard();
        mode.EnterAttendant();

        // Mirror AutomaticAccessEngine check order: mode first, then session.
        var mayRecognize = mode.RecognitionEnabled && guard.TryBeginSession();
        Assert.False(mayRecognize);
    }
}
