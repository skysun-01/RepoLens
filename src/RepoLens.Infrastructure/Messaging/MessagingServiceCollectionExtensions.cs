using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RepoLens.Application.Common.Abstractions.Messaging;
using RepoLens.Infrastructure.Messaging.InMemory;
using RepoLens.Infrastructure.Messaging.ServiceBus;

namespace RepoLens.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static MessagingProvider GetMessagingProvider(this IConfiguration configuration) =>
        configuration.GetSection(MessagingOptions.SectionName).GetValue<MessagingProvider?>(nameof(MessagingOptions.Provider)) ?? MessagingProvider.InMemory;

    /// <summary>Registers <see cref="IMessagePublisher"/> for the configured provider.</summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MessagingOptions>()
            .BindConfiguration(MessagingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<MessageTopology>();

        if (configuration.GetMessagingProvider() == MessagingProvider.ServiceBus)
        {
            services.TryAddSingleton(sp => CreateServiceBusClient(sp.GetRequiredService<IOptions<MessagingOptions>>().Value.ServiceBus));
            services.TryAddSingleton<IMessagePublisher, ServiceBusMessagePublisher>();
        }
        else
        {
            services.TryAddSingleton<InMemoryMessageBus>();
            services.TryAddSingleton<IMessagePublisher, InMemoryMessagePublisher>();
        }

        return services;
    }

    /// <summary>Starts a background consumer that delivers <typeparamref name="TMessage"/> to its registered handler.</summary>
    public static IServiceCollection AddMessageConsumer<TMessage>(this IServiceCollection services, IConfiguration configuration)
        where TMessage : class, IIntegrationMessage
    {
        if (configuration.GetMessagingProvider() == MessagingProvider.ServiceBus)
        {
            services.AddSingleton<IHostedService, ServiceBusConsumer<TMessage>>();
        }
        else
        {
            services.AddSingleton(new InMemoryConsumerRegistration(typeof(TMessage)));
            services.AddSingleton<IHostedService, InMemoryConsumer<TMessage>>();
        }

        return services;
    }

    private static ServiceBusClient CreateServiceBusClient(ServiceBusSettings settings)
    {
        var clientOptions = new ServiceBusClientOptions
        {
            RetryOptions = new ServiceBusRetryOptions { Mode = ServiceBusRetryMode.Exponential, MaxRetries = 5 },
        };

        return string.IsNullOrWhiteSpace(settings.ConnectionString)
            ? new ServiceBusClient(settings.FullyQualifiedNamespace, new DefaultAzureCredential(), clientOptions)
            : new ServiceBusClient(settings.ConnectionString, clientOptions);
    }
}
