using FHT.Access.Application.Services;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Tests;

public class OperatingModeRecognitionGateTests
{
    [Fact]
    public void Attendant_DisablesRecognition()
    {
        var sut = new OperatingModeService();
        sut.EnterAttendant();

        Assert.Equal(AccessOperatingMode.Attendant, sut.Mode);
        Assert.False(sut.RecognitionEnabled);
    }

    [Fact]
    public void Automatic_EnablesRecognition()
    {
        var sut = new OperatingModeService();
        sut.EnterAttendant();
        sut.EnterAutomatic();

        Assert.Equal(AccessOperatingMode.Automatic, sut.Mode);
        Assert.True(sut.RecognitionEnabled);
    }

    [Fact]
    public void EnrollmentAndMaintenance_DisableRecognition()
    {
        var sut = new OperatingModeService();

        sut.EnterEnrollment();
        Assert.False(sut.RecognitionEnabled);

        sut.EnterMaintenance();
        Assert.False(sut.RecognitionEnabled);
    }

    [Fact]
    public void ToggleAttendantAutomatic_RaisesModeChangedAndFlipsGate()
    {
        var sut = new OperatingModeService();
        var observed = new List<bool>();
        sut.ModeChanged += (_, _) => observed.Add(sut.RecognitionEnabled);

        Assert.True(sut.RecognitionEnabled);

        sut.EnterAttendant();
        sut.EnterAutomatic();

        Assert.Equal(new[] { false, true }, observed);
        Assert.True(sut.RecognitionEnabled);
    }
}
