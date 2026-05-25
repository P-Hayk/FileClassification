using FileClassification.Application.Interfaces;
using FileClassification.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FileClassification.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddScoped<IFileClassifier, LanguageDetector>();
        return services;
    }
}
