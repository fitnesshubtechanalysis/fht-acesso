using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using FHT.Access.Application.Abstractions;
using FHT.Access.Application.Dtos;
using FHT.Access.Infrastructure.Settings;

namespace FHT.Access.Infrastructure.Http;

public sealed class GestaoAccessClient : IGestaoAccessClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly AppSettings _settings;
    private string? _accessToken;
    private DateTime? _tokenExpiresAtUtc;

    public GestaoAccessClient(HttpClient http, AppSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<DeviceAuthResult> AuthenticateDeviceAsync(
        string deviceId,
        string deviceSecret,
        CancellationToken ct = default)
    {
        var payload = new { deviceId, deviceSecret };
        using var response = await _http.PostAsJsonAsync(
                Absolute("api/v1/access/device-auth"),
                payload,
                JsonOptions,
                ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DeviceAuthResult>(JsonOptions, ct)
            .ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Device auth returned an empty body.");

        _accessToken = result.AccessToken;
        _tokenExpiresAtUtc = result.ExpiresAt?.Kind switch
        {
            DateTimeKind.Utc => result.ExpiresAt,
            DateTimeKind.Local => result.ExpiresAt.Value.ToUniversalTime(),
            _ when result.ExpiresAt is not null => DateTime.SpecifyKind(result.ExpiresAt.Value, DateTimeKind.Utc),
            _ => DateTime.UtcNow.AddHours(23)
        };
        return result;
    }

    public Task EnsureAuthenticatedAsync(
        string deviceId,
        string deviceSecret,
        CancellationToken ct = default,
        bool force = false)
    {
        if (force || string.IsNullOrWhiteSpace(_accessToken))
            return AuthenticateDeviceAsync(deviceId, deviceSecret, ct);

        if (_tokenExpiresAtUtc is { } exp && exp <= DateTime.UtcNow.AddMinutes(5))
            return AuthenticateDeviceAsync(deviceId, deviceSecret, ct);

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<MemberDto>> GetMembersAsync(
        string unitId,
        DateTime? updatedSince,
        CancellationToken ct = default,
        string? query = null)
    {
        var path = $"api/v1/units/{Uri.EscapeDataString(unitId)}/access/members";
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var trimmed = query.Trim();
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            qs.Add("q=" + Uri.EscapeDataString(digits.Length >= 5 ? digits : trimmed));
        }
        else if (updatedSince is not null)
        {
            qs.Add(
                "updatedSince="
                + Uri.EscapeDataString(updatedSince.Value.ToUniversalTime().ToString("O")));
        }

        if (qs.Count > 0)
            path += "?" + string.Join("&", qs);

        using var request = new HttpRequestMessage(HttpMethod.Get, Absolute(path));
        ApplyAuth(request);

        using var response = await SendWithAuthRetryAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var members = await response.Content.ReadFromJsonAsync<List<MemberDto>>(JsonOptions, ct)
            .ConfigureAwait(false);
        return members ?? [];
    }

    public async Task AcknowledgeEventsAsync(
        string unitId,
        IReadOnlyList<AccessEventDto> events,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        var batch = events.Select(e => new
        {
            eventId = e.Id,
            memberId = e.MemberId,
            direction = e.Direction,
            status = e.Status,
            passageConfirmed = e.PassageConfirmed,
            attemptId = e.AttemptId,
            visitId = e.VisitId,
            occurredAt = e.OccurredAt,
            source = e.Source,
            deviceId = e.DeviceId,
            denialReason = e.DenialReason
        }).ToList();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            Absolute($"api/v1/units/{Uri.EscapeDataString(unitId)}/access/events"))
        {
            Content = JsonContent.Create(batch, options: JsonOptions)
        };
        ApplyAuth(request);

        using var response = await SendWithAuthRetryAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<AccessEvaluateResultDto?> EvaluateAccessAsync(
        string unitId,
        Guid memberId,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            Absolute($"api/v1/units/{Uri.EscapeDataString(unitId)}/access/evaluate"))
        {
            Content = JsonContent.Create(new { memberId = memberId.ToString("D") }, options: JsonOptions)
        };
        ApplyAuth(request);
        using var response = await SendWithAuthRetryAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<AccessEvaluateResultDto>(JsonOptions, ct)
            .ConfigureAwait(false);
    }

    public async Task ConsumeToleranceAsync(
        string unitId,
        Guid memberId,
        Guid? accessEventId,
        string? deviceId,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            Absolute($"api/v1/units/{Uri.EscapeDataString(unitId)}/access/tolerance/consume"))
        {
            Content = JsonContent.Create(new
            {
                memberId = memberId.ToString("D"),
                accessEventId = accessEventId?.ToString("D"),
                deviceId
            }, options: JsonOptions)
        };
        ApplyAuth(request);
        using var response = await SendWithAuthRetryAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task RecordBlockedAttemptAsync(
        string unitId,
        Guid memberId,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            Absolute($"api/v1/units/{Uri.EscapeDataString(unitId)}/access/tolerance/blocked-attempt"))
        {
            Content = JsonContent.Create(new { memberId = memberId.ToString("D") }, options: JsonOptions)
        };
        ApplyAuth(request);
        using var response = await SendWithAuthRetryAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> UploadMemberPhotoAsync(
        string unitId,
        Guid memberId,
        byte[] jpeg,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        if (jpeg.Length == 0)
            throw new ArgumentException("JPEG vazio.", nameof(jpeg));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            Absolute(
                $"api/v1/units/{Uri.EscapeDataString(unitId)}/access/members/{memberId:D}/photo"))
        {
            Content = new ByteArrayContent(jpeg)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        ApplyAuth(request);

        using var response = await SendWithAuthRetryAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content
            .ReadFromJsonAsync<PhotoUploadResponse>(JsonOptions, ct)
            .ConfigureAwait(false);
        return body?.PhotoUrl
               ?? throw new InvalidOperationException("Photo upload returned empty photoUrl.");
    }

    private async Task<HttpResponseMessage> SendWithAuthRetryAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();
        _accessToken = null;
        _tokenExpiresAtUtc = null;
        throw new UnauthorizedAccessException("Gestão JWT expirado ou inválido — reautentique o dispositivo.");
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        var token = _accessToken;
        if (string.IsNullOrWhiteSpace(token))
            return;

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private Uri Absolute(string relativePath)
    {
        var origin = GestaoUrl.ResolveBaseAddress(_settings.GestaoBaseUrl);
        return new Uri(origin, relativePath);
    }

    private sealed class PhotoUploadResponse
    {
        public string? PhotoUrl { get; set; }
    }
}
