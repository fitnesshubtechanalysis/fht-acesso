using FHT.Access.Application.Abstractions;
using FHT.Access.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace FHT.Access.Application.Services;

/// <summary>
/// Silent exit-lane observer: captures and identifies faces for frequency / error-rate
/// metrics only. Never touches the kiosk UI, state machine, presence, or turnstile.
/// </summary>
public sealed class ExitObservationService : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ApproachHold = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan CooldownAfterAttempt = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MetricsLogInterval = TimeSpan.FromMinutes(5);
    private const int IdentifyAttempts = 8;

    private readonly RecognitionService _recognition;
    private readonly IDiagnosticLog? _log;
    private readonly ILogger<ExitObservationService>? _logger;

    private Func<byte[]?>? _captureJpeg;
    private Func<bool>? _hasMotionHint;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _started;

    private long _attempts;
    private long _matched;
    private long _missed;
    private DateTime _lastMetricsLogUtc = DateTime.MinValue;

    public ExitObservationService(
        RecognitionService recognition,
        IDiagnosticLog? log = null,
        ILogger<ExitObservationService>? logger = null)
    {
        _recognition = recognition;
        _log = log;
        _logger = logger;
    }

    public long Attempts => Interlocked.Read(ref _attempts);
    public long Matched => Interlocked.Read(ref _matched);
    public long Missed => Interlocked.Read(ref _missed);

    public void BindCamera(Func<byte[]?> captureJpeg, Func<bool> hasMotionHint)
    {
        _captureJpeg = captureJpeg;
        _hasMotionHint = hasMotionHint;
    }

    public void Start()
    {
        if (_started)
            return;
        if (_captureJpeg is null || _hasMotionHint is null)
            throw new InvalidOperationException("Exit observation camera not bound.");

        _started = true;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
        _log?.Information("Exit observation started (silent background capture).");
    }

    public async ValueTask DisposeAsync()
    {
        if (!_started)
            return;

        _started = false;
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignore
        }

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Exit observation loop ended with error.");
            }
        }

        _cts?.Dispose();
        _cts = null;
        _loop = null;
        LogMetrics(force: true);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        DateTime? approachStarted = null;
        var skipUntil = DateTime.MinValue;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                LogMetrics(force: false);

                if (DateTime.UtcNow < skipUntil)
                {
                    await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                    continue;
                }

                var motion = false;
                try
                {
                    motion = _hasMotionHint?.Invoke() == true;
                }
                catch
                {
                    motion = false;
                }

                if (!motion)
                {
                    approachStarted = null;
                    await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                    continue;
                }

                approachStarted ??= DateTime.UtcNow;
                if (DateTime.UtcNow - approachStarted.Value < ApproachHold)
                {
                    await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                    continue;
                }

                approachStarted = null;
                await ObserveOnceAsync(ct).ConfigureAwait(false);
                skipUntil = DateTime.UtcNow.Add(CooldownAfterAttempt);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Exit observation tick failed.");
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task ObserveOnceAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _attempts);
        FaceMatchResult? best = null;

        for (var i = 0; i < IdentifyAttempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            byte[]? frame = null;
            try
            {
                frame = _captureJpeg?.Invoke();
            }
            catch
            {
                frame = null;
            }

            if (frame is null || frame.Length < 100)
            {
                await Task.Delay(150, ct).ConfigureAwait(false);
                continue;
            }

            FaceMatchResult? match = null;
            try
            {
                match = await _recognition
                    .IdentifyOnlyAsync(frame, ct, FaceDetectionOptions.ExitDistance)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Unrecognized / engine hiccup — count as miss path below if nothing matched.
            }

            if (match is not null && (best is null || match.Score > best.Score))
                best = match;

            if (best is not null && best.Score >= 0.82)
                break;

            await Task.Delay(180, ct).ConfigureAwait(false);
        }

        if (best is not null)
        {
            Interlocked.Increment(ref _matched);
            _log?.Information(
                $"Exit observe match member={best.MemberId} score={best.Score:F3} " +
                $"(attempts={Attempts} matched={Matched} missed={Missed})");
        }
        else
        {
            Interlocked.Increment(ref _missed);
            // Misses are expected (free exit / strangers). Keep at debug volume via aggregate metrics.
        }
    }

    private void LogMetrics(bool force)
    {
        if (!force && DateTime.UtcNow - _lastMetricsLogUtc < MetricsLogInterval)
            return;

        var attempts = Attempts;
        if (attempts <= 0 && !force)
            return;

        _lastMetricsLogUtc = DateTime.UtcNow;
        var matched = Matched;
        var missed = Missed;
        var missRate = attempts > 0 ? (100.0 * missed / attempts) : 0;
        _log?.Information(
            $"Exit observe metrics: attempts={attempts} matched={matched} missed={missed} missRate={missRate:F1}%");
    }
}
