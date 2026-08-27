using FHT.Access.Domain.Abstractions;

namespace FHT.Access.Face;

/// <summary>
/// CI / offline stub: enroll keeps templates in memory; identify never matches.
/// </summary>
public sealed class StubFaceRecognitionService : IFaceRecognitionService
{
    private readonly Dictionary<Guid, byte[]> _templates = new();
    private readonly object _sync = new();

    public string ModelVersion => "stub-v0";

    public Task EnrollAsync(Guid memberId, byte[] imageBgrOrJpeg, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imageBgrOrJpeg);
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _templates[memberId] = (byte[])imageBgrOrJpeg.Clone();
        }

        return Task.CompletedTask;
    }

    public Task<FaceMatchResult?> IdentifyAsync(byte[] imageBgrOrJpeg, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imageBgrOrJpeg);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<FaceMatchResult?>(null);
    }

    public Task RemoveAsync(Guid memberId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _templates.Remove(memberId);
        }

        return Task.CompletedTask;
    }
}
