namespace FHT.Access.Application.Services;

/// <summary>Cinco códigos desconhecidos seguidos travam o teclado por um minuto.</summary>
public sealed class PinKeypadGuard
{
    public const int FailureLimit = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromSeconds(60);

    private int _failures;
    private DateTime _lockedUntilUtc = DateTime.MinValue;

    public bool IsLocked(DateTime utcNow) => utcNow < _lockedUntilUtc;

    public TimeSpan Remaining(DateTime utcNow)
        => IsLocked(utcNow) ? _lockedUntilUtc - utcNow : TimeSpan.Zero;

    public void RegisterFailure(DateTime utcNow)
    {
        if (IsLocked(utcNow))
            return;
        _failures++;
        if (_failures < FailureLimit)
            return;
        _failures = 0;
        _lockedUntilUtc = utcNow.Add(LockDuration);
    }

    public void RegisterSuccess() => _failures = 0;
}
