namespace FHT.Access.Application.Services;

/// <summary>
/// Prevents continuous re-recognition of the same person standing in front of the camera.
/// </summary>
public sealed class RecognitionSessionGuard
{
    private readonly object _sync = new();
    private Guid? _sessionId;
    private DateTime _sessionStartedUtc;
    private DateTime _cooldownUntilUtc;
    private bool _facePresent;

    public TimeSpan ResultDisplayDuration { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan CooldownAfterSession { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan MaxSessionDuration { get; set; } = TimeSpan.FromSeconds(45);

    public bool IsInCooldown
    {
        get
        {
            lock (_sync)
                return DateTime.UtcNow < _cooldownUntilUtc;
        }
    }

    public bool HasActiveSession
    {
        get
        {
            lock (_sync)
                return _sessionId is not null;
        }
    }

    /// <summary>Returns true if a new recognition attempt may start.</summary>
    public bool TryBeginSession()
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            if (now < _cooldownUntilUtc)
                return false;
            if (_sessionId is not null)
                return false;

            _sessionId = Guid.NewGuid();
            _sessionStartedUtc = now;
            _facePresent = true;
            return true;
        }
    }

    public void MarkFacePresent(bool present)
    {
        lock (_sync)
        {
            _facePresent = present;
            if (!present && _sessionId is not null)
            {
                // Face left — end session into cooldown so next person can be processed.
                EndSessionUnlocked();
            }
        }
    }

    public void CompleteSession()
    {
        lock (_sync)
        {
            EndSessionUnlocked();
        }
    }

    public bool IsSessionExpired()
    {
        lock (_sync)
        {
            if (_sessionId is null)
                return false;
            return DateTime.UtcNow - _sessionStartedUtc > MaxSessionDuration;
        }
    }

    private void EndSessionUnlocked()
    {
        _sessionId = null;
        _cooldownUntilUtc = DateTime.UtcNow + CooldownAfterSession;
        _facePresent = false;
    }

    /// <summary>Ends the session with no cooldown so the person in front of the camera can retry immediately.</summary>
    public void AllowImmediateRetry()
    {
        lock (_sync)
        {
            _sessionId = null;
            _cooldownUntilUtc = DateTime.MinValue;
            _facePresent = false;
        }
    }
}
