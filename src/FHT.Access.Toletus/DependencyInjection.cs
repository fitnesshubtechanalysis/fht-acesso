using FHT.Access.Domain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FHT.Access.Toletus;

public static class DependencyInjection
{
    public static IServiceCollection RegisterFake(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ITurnstile, FakeTurnstile>();
        return services;
    }

    public static IServiceCollection RegisterToletus(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ITurnstile, ToletusLiteNetTurnstile>();
        return services;
    }

    /// <summary>Register with diagnostic logging callbacks (preferred in the WPF host).</summary>
    public static IServiceCollection RegisterToletus(
        this IServiceCollection services,
        Action<string> information,
        Action<string>? error = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(information);
        services.AddSingleton<ITurnstile>(_ => new ToletusLiteNetTurnstile(information, error ?? information));
        return services;
    }
}
