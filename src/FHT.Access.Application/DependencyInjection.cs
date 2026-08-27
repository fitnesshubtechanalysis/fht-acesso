using FHT.Access.Application.Services;

using Microsoft.Extensions.DependencyInjection;



namespace FHT.Access.Application;



public static class DependencyInjection

{

    public static IServiceCollection AddFhtAccessApplication(this IServiceCollection services)

    {

        ArgumentNullException.ThrowIfNull(services);



        services.AddSingleton<AccessDecisionEvaluator>();
        services.AddSingleton<AccessDecisionService>();

        services.AddSingleton<AccessEventService>();

        services.AddSingleton<TurnstileService>();

        services.AddSingleton<MemberSyncService>();

        services.AddSingleton<OfflineSyncService>();

        // MemberPhotoSyncService registered in Infrastructure (needs data directory).

        services.AddSingleton<BackgroundSyncService>();

        services.AddSingleton<RecognitionService>();

        services.AddSingleton<PresenceService>();

        services.AddSingleton<PresenceBootstrapService>();

        services.AddSingleton<VisitExpiryService>();

        services.AddSingleton<TurnstileConnectionSupervisor>();

        services.AddSingleton<AccessFlowService>();

        services.AddSingleton<DeviceService>();



        services.AddSingleton<OperatingModeService>();

        services.AddSingleton<AccessStateMachine>();

        services.AddSingleton<CameraCoordinator>();

        services.AddSingleton<RecognitionSessionGuard>();

        services.AddSingleton<AutomaticAccessEngine>();

        services.AddSingleton<AttendantSessionService>();



        return services;

    }

}

