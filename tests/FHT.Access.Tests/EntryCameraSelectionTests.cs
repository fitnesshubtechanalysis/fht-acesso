using FHT.Access.Application.Services;

namespace FHT.Access.Tests;

public class EntryCameraSelectionTests
{
    [Fact]
    public void Uses_the_only_camera_that_answers_on_any_port()
    {
        var chosen = EntryCameraSelection.Choose(preferredIndex: 0, exitIndex: 1, workingIndices: [2]);
        Assert.Equal(2, chosen);
    }

    [Fact]
    public void Keeps_the_saved_entry_camera_when_it_still_answers()
    {
        var chosen = EntryCameraSelection.Choose(preferredIndex: 0, exitIndex: 1, workingIndices: [0, 1]);
        Assert.Equal(0, chosen);
    }

    [Fact]
    public void Skips_the_exit_index_when_the_saved_entry_port_is_dead()
    {
        var chosen = EntryCameraSelection.Choose(preferredIndex: 0, exitIndex: 1, workingIndices: [1, 3]);
        Assert.Equal(3, chosen);
    }
}
