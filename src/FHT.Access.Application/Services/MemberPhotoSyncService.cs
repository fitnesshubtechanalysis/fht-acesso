using System.Text.Json;
using FHT.Access.Application.Abstractions;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;

namespace FHT.Access.Application.Services;

/// <summary>
/// Queues enrolled face JPEGs and uploads them to Gestão as Customer.photoUrl.
/// </summary>
public sealed class MemberPhotoSyncService
{
    public const string SyncKindMemberPhoto = "member_photo";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IPendingSyncRepository _pendingSync;
    private readonly IGestaoAccessClient _client;
    private readonly IMemberRepository _members;
    private readonly string _pendingPhotosDir;

    public MemberPhotoSyncService(
        IPendingSyncRepository pendingSync,
        IGestaoAccessClient client,
        IMemberRepository members,
        string? dataDirectory = null)
    {
        _pendingSync = pendingSync;
        _client = client;
        _members = members;
        var dataDir = string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FHT",
                "Access")
            : dataDirectory;
        _pendingPhotosDir = Path.Combine(dataDir, "pending-photos");
        Directory.CreateDirectory(_pendingPhotosDir);
    }

    public async Task EnqueueAsync(
        Guid memberId,
        byte[] jpeg,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        var id = Guid.NewGuid();
        var filePath = Path.Combine(_pendingPhotosDir, $"{id:D}.jpg");
        await File.WriteAllBytesAsync(filePath, jpeg, cancellationToken).ConfigureAwait(false);

        var payload = new PhotoPayload(memberId, Path.GetFileName(filePath));
        var pending = new PendingSync
        {
            Id = id,
            Kind = SyncKindMemberPhoto,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            CreatedAt = DateTime.UtcNow,
            Attempts = 0
        };
        await _pendingSync.EnqueueAsync(pending, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Try immediate upload; on failure the item stays queued for background flush.</summary>
    public async Task EnqueueAndTryUploadAsync(
        string unitId,
        Guid memberId,
        byte[] jpeg,
        CancellationToken cancellationToken = default)
    {
        await EnqueueAsync(memberId, jpeg, cancellationToken).ConfigureAwait(false);
        try
        {
            await FlushAsync(unitId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Background sync will retry.
        }
    }

    public async Task<int> FlushAsync(string unitId, CancellationToken cancellationToken = default)
    {
        var photos = await _pendingSync
            .GetPendingByKindAsync(SyncKindMemberPhoto, take: 50, cancellationToken)
            .ConfigureAwait(false);
        if (photos.Count == 0)
            return 0;

        const int maxAttempts = 12;
        var uploaded = 0;
        foreach (var item in photos)
        {
            try
            {
                if (item.Attempts >= maxAttempts)
                {
                    await _pendingSync.RemoveAsync(item.Id, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var payload = JsonSerializer.Deserialize<PhotoPayload>(item.PayloadJson, JsonOptions);
                if (payload is null || string.IsNullOrWhiteSpace(payload.FileName))
                {
                    await _pendingSync.RemoveAsync(item.Id, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var filePath = Path.Combine(_pendingPhotosDir, payload.FileName);
                if (!File.Exists(filePath))
                {
                    // Arquivo sumiu — remove da fila para não prender o LIMIT.
                    await _pendingSync.RemoveAsync(item.Id, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var jpeg = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
                var photoUrl = await _client
                    .UploadMemberPhotoAsync(unitId, payload.MemberId, jpeg, cancellationToken)
                    .ConfigureAwait(false);

                var member = await _members.GetByIdAsync(payload.MemberId, cancellationToken)
                    .ConfigureAwait(false);
                if (member is not null)
                {
                    member.PhotoUrl = photoUrl;
                    member.UpdatedAt = DateTime.UtcNow;
                    await _members.UpsertAsync(member, cancellationToken).ConfigureAwait(false);
                }

                await _pendingSync.RemoveAsync(item.Id, cancellationToken).ConfigureAwait(false);
                try { File.Delete(filePath); } catch { /* ignore */ }
                uploaded++;
            }
            catch (Exception ex)
            {
                await _pendingSync.MarkAttemptAsync(item.Id, ex.Message, cancellationToken)
                    .ConfigureAwait(false);
                if (item.Attempts + 1 >= maxAttempts)
                    await _pendingSync.RemoveAsync(item.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        return uploaded;
    }

    private sealed record PhotoPayload(Guid MemberId, string FileName);
}
