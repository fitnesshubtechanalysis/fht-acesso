using System.Text.Json;
using System.Text.Json.Serialization;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;

namespace FHT.Access.Infrastructure.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly object _gate = new();

    public JsonSettingsStore(string? settingsDirectory = null)
    {
        var programDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FHT",
            "Access");
        var localAppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FHT",
            "Access");

        var dir = settingsDirectory ?? programDataDir;
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "appsettings.json");

        if (settingsDirectory is null)
            MigrateFromLocalAppDataIfNeeded(localAppDataDir, programDataDir);
    }

    public string FilePath => _filePath;

    private static void MigrateFromLocalAppDataIfNeeded(string localDir, string programDataDir)
    {
        var localFile = Path.Combine(localDir, "appsettings.json");
        var targetFile = Path.Combine(programDataDir, "appsettings.json");
        if (!File.Exists(localFile) || File.Exists(targetFile))
            return;

        Directory.CreateDirectory(programDataDir);
        File.Copy(localFile, targetFile, overwrite: false);
        var bak = targetFile + ".bak";
        try { File.Copy(localFile, bak, overwrite: true); } catch { /* ignore */ }
    }

    public AppSettings LoadAppSettings()
    {
        lock (_gate)
        {
            return ReadUnlocked();
        }
    }

    public void SaveAppSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            WriteUnlocked(settings);
        }
    }

    public Task<AppSettings> LoadAppSettingsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(LoadAppSettings());

    public Task SaveAppSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        SaveAppSettings(settings);
        return Task.CompletedTask;
    }

    public Task<Device?> GetDeviceAsync(CancellationToken cancellationToken = default)
    {
        var settings = LoadAppSettings();
        if (settings.Device is null)
        {
            if (string.IsNullOrWhiteSpace(settings.DeviceId) && string.IsNullOrWhiteSpace(settings.UnitId))
                return Task.FromResult<Device?>(null);

            return Task.FromResult<Device?>(new Device
            {
                Id = Guid.TryParse(settings.DeviceId, out var id) ? id : Guid.Empty,
                Name = settings.DeviceId,
                UnitId = settings.UnitId,
                Serial = settings.TurnstileSerial,
                IpAddress = string.IsNullOrWhiteSpace(settings.TurnstileIp) ? null : settings.TurnstileIp
            });
        }

        return Task.FromResult<Device?>(new Device
        {
            Id = settings.Device.Id,
            Name = settings.Device.Name,
            UnitId = settings.Device.UnitId,
            Serial = settings.Device.Serial,
            IpAddress = settings.Device.IpAddress
        });
    }

    public Task SaveDeviceAsync(Device device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        var settings = LoadAppSettings();
        settings.Device = new DeviceSettings
        {
            Id = device.Id,
            Name = device.Name,
            UnitId = device.UnitId,
            Serial = device.Serial,
            IpAddress = device.IpAddress
        };
        settings.DeviceId = device.Id == Guid.Empty ? settings.DeviceId : device.Id.ToString("D");
        settings.UnitId = string.IsNullOrWhiteSpace(device.UnitId) ? settings.UnitId : device.UnitId;
        SaveAppSettings(settings);
        return Task.CompletedTask;
    }

    public Task<TurnstileConfig?> GetTurnstileConfigAsync(CancellationToken cancellationToken = default)
    {
        var settings = LoadAppSettings();
        if (string.IsNullOrWhiteSpace(settings.TurnstileNetwork)
            && string.IsNullOrWhiteSpace(settings.TurnstileIp)
            && string.IsNullOrWhiteSpace(settings.TurnstileSerial)
            && !settings.UseFakeTurnstile)
        {
            return Task.FromResult<TurnstileConfig?>(null);
        }

        return Task.FromResult<TurnstileConfig?>(new TurnstileConfig
        {
            NetworkInterface = settings.TurnstileNetwork,
            BoardIp = settings.TurnstileIp,
            Serial = settings.TurnstileSerial,
            UseFake = settings.UseFakeTurnstile
        });
    }

    public Task SaveTurnstileConfigAsync(TurnstileConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var settings = LoadAppSettings();
        settings.TurnstileNetwork = config.NetworkInterface;
        settings.TurnstileIp = config.BoardIp;
        settings.TurnstileSerial = config.Serial;
        settings.UseFakeTurnstile = config.UseFake;
        SaveAppSettings(settings);
        return Task.CompletedTask;
    }

    public Task<SyncState> GetSyncStateAsync(CancellationToken cancellationToken = default)
    {
        var settings = LoadAppSettings();
        if (settings.SyncState is null)
            return Task.FromResult(new SyncState());

        return Task.FromResult(new SyncState
        {
            LastMembersSyncAt = settings.SyncState.LastMembersSyncAt,
            LastEventsSyncAt = settings.SyncState.LastEventsSyncAt,
            Cursor = settings.SyncState.Cursor
        });
    }

    public Task SaveSyncStateAsync(SyncState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var settings = LoadAppSettings();
        settings.SyncState = new SyncStateSettings
        {
            LastMembersSyncAt = state.LastMembersSyncAt,
            LastEventsSyncAt = state.LastEventsSyncAt,
            Cursor = state.Cursor
        };
        SaveAppSettings(settings);
        return Task.CompletedTask;
    }

    private AppSettings ReadUnlocked()
    {
        if (!File.Exists(_filePath))
        {
            var defaults = CreateDefaults();
            WriteUnlocked(defaults);
            return defaults;
        }

        var json = File.ReadAllText(_filePath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefaults();
        if (settings.FaceMatchThreshold > 0.7)
        {
            settings.FaceMatchThreshold = 0.35;
            WriteUnlocked(settings);
        }

        return settings;
    }

    private void WriteUnlocked(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.DataDirectory))
        {
            settings.DataDirectory = Path.GetDirectoryName(_filePath) ?? string.Empty;
        }

        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);

        if (File.Exists(_filePath))
        {
            try { File.Copy(_filePath, _filePath + ".bak", overwrite: true); } catch { /* ignore */ }
        }

        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, _filePath, overwrite: true);
    }

    private AppSettings CreateDefaults()
    {
        var dir = Path.GetDirectoryName(_filePath) ?? string.Empty;
        return new AppSettings
        {
            DataDirectory = dir,
            UseFakeTurnstile = true,
            FaceMatchThreshold = 0.35,
            AdminPin = "1234",
            KioskPortrait = true,
            CameraWidth = 1920,
            CameraHeight = 1080,
            CameraFps = 30,
            ProcessFps = 8,
            PassageTimeoutSec = 10,
            RecognitionCooldownSec = 3,
            VisitMaxHours = 12,
            StartupDelaySec = 8,
            SyncState = new SyncStateSettings()
        };
    }
}
