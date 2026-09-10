using FHT.Access.Application.Abstractions;
using FHT.Access.Application.Dtos;
using FHT.Access.Application.Services;
using FHT.Access.Infrastructure.Settings;

namespace FHT.Access.App.Services;

/// <summary>
/// Aplica allowlist remota em <see cref="AppSettings"/> + runtime (flow/presence) e grava disco.
/// </summary>
public sealed class RemoteConfigApplier : IRemoteConfigApplier
{
    private readonly AppSettings _settings;
    private readonly JsonSettingsStore _store;
    private readonly AccessFlowService _flow;
    private readonly PresenceService _presence;
    private readonly GateLaneEngineHost? _gates;
    private readonly IDiagnosticLog? _log;
    private readonly object _gate = new();

    public RemoteConfigApplier(
        AppSettings settings,
        JsonSettingsStore store,
        AccessFlowService flow,
        PresenceService presence,
        GateLaneEngineHost? gates = null,
        IDiagnosticLog? log = null)
    {
        _settings = settings;
        _store = store;
        _flow = flow;
        _presence = presence;
        _gates = gates;
        _log = log;
    }

    public int LastAckedConfigVersion
    {
        get
        {
            lock (_gate)
                return _settings.RemoteConfigAckVersion;
        }
    }

    public RemoteConfigApplyResult ApplyIfNewer(DeviceRemoteConfigDto? config)
    {
        lock (_gate)
        {
            var version = config?.ConfigVersion ?? 0;
            var applied = false;

            if (config is not null && version > _settings.RemoteConfigAckVersion)
            {
                ApplyAllowlist(config);
                ApplyRuntime();
                _settings.RemoteConfigAckVersion = version;
                _store.SaveAppSettings(_settings);
                applied = true;
                _log?.Information($"Config remota v{version} aplicada e gravada em appsettings.");
            }

            return new RemoteConfigApplyResult(applied, _settings.RemoteConfigAckVersion, BuildReported());
        }
    }

    private void ApplyAllowlist(DeviceRemoteConfigDto config)
    {
        if (config.FreeGateMode is bool freeGate)
            _settings.FreeGateMode = freeGate;
        if (!string.IsNullOrWhiteSpace(config.ExitMode))
            _settings.ExitMode = config.ExitMode.Trim();
        if (config.PassageTimeoutSec is int pts && pts > 0)
            _settings.PassageTimeoutSec = pts;
        if (config.PassageSuccessDisplaySec is int psd && psd > 0)
            _settings.PassageSuccessDisplaySec = psd;
        if (config.PassageReleaseMinDisplaySec is int prd && prd >= 0)
            _settings.PassageReleaseMinDisplaySec = prd;
        if (config.RecognitionCooldownSec is int rcs && rcs >= 0)
            _settings.RecognitionCooldownSec = rcs;
        if (config.VisitMaxHours is int vmh && vmh > 0)
            _settings.VisitMaxHours = vmh;
        if (config.FaceMatchThreshold is double fmt && fmt > 0)
            _settings.FaceMatchThreshold = fmt;
        if (config.UseFakeTurnstile is bool fake)
            _settings.UseFakeTurnstile = fake;
        if (config.TurnstileIp is not null)
            _settings.TurnstileIp = config.TurnstileIp;
        if (config.TurnstileSerial is not null)
            _settings.TurnstileSerial = config.TurnstileSerial;
        if (config.WebcamIndex is int wi && wi >= 0)
            _settings.WebcamIndex = wi;
        if (config.WebcamIndexExit is int wie)
            _settings.WebcamIndexExit = wie;
        if (config.CameraFlipHorizontal is bool cfh)
            _settings.CameraFlipHorizontal = cfh;
        if (config.CameraFlipVertical is bool cfv)
            _settings.CameraFlipVertical = cfv;
        if (config.CameraRotateDegrees is int rot)
            _settings.CameraRotateDegrees = rot;
    }

    private void ApplyRuntime()
    {
        _flow.FreeGateMode = _settings.FreeGateMode;
        _presence.FreeGateMode = _settings.FreeGateMode;
        _flow.PassageTimeout = TimeSpan.FromSeconds(
            _settings.PassageTimeoutSec <= 0 ? 10 : _settings.PassageTimeoutSec);
        _presence.RecognitionCooldown = TimeSpan.FromSeconds(
            _settings.RecognitionCooldownSec <= 0 ? 3 : _settings.RecognitionCooldownSec);
        _presence.StalePendingThreshold = _flow.PassageTimeout + TimeSpan.FromSeconds(2);
        _presence.VisitMaxDuration = TimeSpan.FromHours(
            _settings.VisitMaxHours <= 0 ? 12 : _settings.VisitMaxHours);

        var dualGate = _settings.WebcamIndexExit >= 0
                       && _settings.WebcamIndexExit != _settings.WebcamIndex;
        var dualFacial = dualGate
                         && string.Equals(_settings.ExitMode, "facial", StringComparison.OrdinalIgnoreCase);
        _flow.EntryOnlyMode = !dualFacial;
        _flow.DualGateMode = dualFacial;
        _presence.EntryOnlyMode = _flow.EntryOnlyMode;
        _presence.DualGateMode = dualFacial;

        if (_gates is not null)
        {
            var successDisplay = TimeSpan.FromSeconds(
                _settings.PassageSuccessDisplaySec <= 0 ? 5 : _settings.PassageSuccessDisplaySec);
            var releaseMinDisplay = TimeSpan.FromSeconds(
                _settings.PassageReleaseMinDisplaySec <= 0 ? 3 : _settings.PassageReleaseMinDisplaySec);
            _gates.ConfigureKioskDisplay(successDisplay, releaseMinDisplay);
        }
    }

    private IReadOnlyDictionary<string, object?> BuildReported()
    {
        return new Dictionary<string, object?>
        {
            ["freeGateMode"] = _settings.FreeGateMode,
            ["exitMode"] = _settings.ExitMode,
            ["passageTimeoutSec"] = _settings.PassageTimeoutSec,
            ["passageSuccessDisplaySec"] = _settings.PassageSuccessDisplaySec,
            ["passageReleaseMinDisplaySec"] = _settings.PassageReleaseMinDisplaySec,
            ["recognitionCooldownSec"] = _settings.RecognitionCooldownSec,
            ["visitMaxHours"] = _settings.VisitMaxHours,
            ["faceMatchThreshold"] = _settings.FaceMatchThreshold,
            ["useFakeTurnstile"] = _settings.UseFakeTurnstile,
            ["turnstileIp"] = _settings.TurnstileIp,
            ["turnstileSerial"] = _settings.TurnstileSerial,
            ["webcamIndex"] = _settings.WebcamIndex,
            ["webcamIndexExit"] = _settings.WebcamIndexExit,
            ["cameraFlipHorizontal"] = _settings.CameraFlipHorizontal,
            ["cameraFlipVertical"] = _settings.CameraFlipVertical,
            ["cameraRotateDegrees"] = _settings.CameraRotateDegrees,
            ["configVersion"] = _settings.RemoteConfigAckVersion,
        };
    }
}
