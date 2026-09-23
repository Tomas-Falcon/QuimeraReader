using Microsoft.Extensions.DependencyInjection;
using QuimeraReader.Shared.Interfaces;
using QuimeraReader.Shared.Services;

namespace QuimeraReader.Shared.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddQuimeraServices(this IServiceCollection services)
    {
        services.AddScoped<ITranslationService, TranslationService>();
        return services;
    }
}