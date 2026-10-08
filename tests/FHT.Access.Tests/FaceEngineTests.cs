using FHT.Access.Face;

namespace FHT.Access.Tests;

public class FaceEngineTests
{
    [Fact]
    public void Only_arcface_templates_are_loaded()
    {
        Assert.True(LocalHistogramFaceService.CanHydrate(LocalHistogramFaceService.ArcFaceModelVersion));
        Assert.False(LocalHistogramFaceService.CanHydrate(LocalHistogramFaceService.ArcFaceUnalignedVersion));
        Assert.False(LocalHistogramFaceService.CanHydrate(LocalHistogramFaceService.ArcFaceBackgroundVersion));
        Assert.False(LocalHistogramFaceService.CanHydrate(LocalHistogramFaceService.ArcFaceFullFrameVersion));
        Assert.False(LocalHistogramFaceService.CanHydrate(LocalHistogramFaceService.ArcFaceHaarEyesVersion));
        Assert.False(LocalHistogramFaceService.CanHydrate(LocalHistogramFaceService.ArcFaceCollapsedVersion));
        Assert.False(LocalHistogramFaceService.CanHydrate(LocalHistogramFaceService.SfaceModelVersion));
        Assert.False(LocalHistogramFaceService.CanHydrate(LocalHistogramFaceService.SpatialModelVersion));
        Assert.False(LocalHistogramFaceService.CanHydrate(LocalHistogramFaceService.HistModelVersion));
        Assert.False(LocalHistogramFaceService.CanHydrate(null));
    }

    [Fact]
    public async Task ArcFace_model_loads_when_the_file_is_present()
    {
        var dir = FindModelsDirectory();
        if (dir is null)
            return;
        var model = Path.Combine(dir, "arcfaceresnet100-8.onnx");
        if (new FileInfo(model).Length < 20_000_000)
            return;

        var service = new LocalHistogramFaceService(0.72, modelDirectory: dir);
        var match = await service.IdentifyAsync(new byte[256]);
        Assert.Null(match);
        Assert.NotEqual("Modelo ArcFace não encontrado", service.LastIdentifyNote);
        service.Dispose();
    }

    [Fact]
    public void Probe_saved_aligned_faces()
    {
        var shots = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FHT", "Access", "face-debug");
        var jorge = Path.Combine(shots, "20261008-032347-405-modelo.jpg");
        var laurenA = Path.Combine(shots, "20261008-032507-208-modelo.jpg");
        var laurenB = Path.Combine(shots, "20261008-032510-244-modelo.jpg");
        if (!File.Exists(jorge) || !File.Exists(laurenA) || !File.Exists(laurenB))
            return;

        var dir = FindModelsDirectory();
        if (dir is null)
            return;

        var service = new LocalHistogramFaceService(0.72, modelDirectory: dir);
        var cross = service.CompareJpegs(File.ReadAllBytes(jorge), File.ReadAllBytes(laurenB));
        var same = service.CompareJpegs(File.ReadAllBytes(laurenA), File.ReadAllBytes(laurenB));
        service.Dispose();
        Assert.True(cross < 0.40, $"pessoas diferentes ficaram em {cross:F3}");
        Assert.True(same > 0.70, $"a mesma pessoa ficou em {same:F3}");
    }

    private static string? FindModelsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "FHT.Access.Face", "models", "arcfaceresnet100-8.onnx");
            if (File.Exists(candidate))
                return Path.GetDirectoryName(candidate);
            dir = dir.Parent;
        }

        return null;
    }
}
