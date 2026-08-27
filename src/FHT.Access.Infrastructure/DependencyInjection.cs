using FHT.Access.Application.Abstractions;
using FHT.Access.Application.Services;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Infrastructure.Http;
using FHT.Access.Infrastructure.Logging;
using FHT.Access.Infrastructure.Persistence;
using FHT.Access.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace FHT.Access.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFhtAccessInfrastructure(
        this IServiceCollection services,
        string? settingsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var store = new JsonSettingsStore(settingsDirectory);
        var appSettings = store.LoadAppSettings();

        var dataDir = string.IsNullOrWhiteSpace(appSettings.DataDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "FHT",
                "Access")
            : appSettings.DataDirectory;
        Directory.CreateDirectory(dataDir);

        var dbPath = Path.Combine(dataDir, "access.db");
        var factory = new SqliteConnectionFactory(dbPath);

        services.AddSingleton(appSettings);
        services.AddSingleton<IAccessDeviceContext, AppSettingsDeviceContext>();
        services.AddSingleton(store);
        services.AddSingleton<ISettingsStore>(store);
        services.AddSingleton(factory);
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<IMemberRepository, MemberRepository>();
        services.AddSingleton<IPresenceRepository, PresenceRepository>();
        services.AddSingleton<IAccessAttemptRepository, AccessAttemptRepository>();
        services.AddSingleton<IVisitRepository, VisitRepository>();
        services.AddSingleton<IPresenceCorrectionRepository, PresenceCorrectionRepository>();
        services.AddSingleton<IAccessEventRepository>(sp =>
            new AccessEventRepository(
                sp.GetRequiredService<SqliteConnectionFactory>(),
                sp.GetRequiredService<IPresenceRepository>()));
        services.AddSingleton<IPendingSyncRepository, PendingSyncRepository>();
        services.AddSingleton(sp => new FileLogger(
            Path.Combine(dataDir, "logs"),
            sp.GetRequiredService<SqliteConnectionFactory>()));
        services.AddSingleton<IDiagnosticLog>(sp => sp.GetRequiredService<FileLogger>());

        services.AddSingleton<IGestaoAccessClient>(sp =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            return new GestaoAccessClient(http, sp.GetRequiredService<AppSettings>());
        });

        services.AddSingleton(sp => new MemberPhotoSyncService(
            sp.GetRequiredService<IPendingSyncRepository>(),
            sp.GetRequiredService<IGestaoAccessClient>(),
            sp.GetRequiredService<IMemberRepository>(),
            dataDir));

        return services;
    }
}
