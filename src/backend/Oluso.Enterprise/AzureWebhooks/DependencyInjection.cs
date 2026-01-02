using Azure.Identity;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Events;
using Oluso.Webhooks;

namespace Oluso.Enterprise.AzureWebhooks;

/// <summary>
/// Extension methods for registering Azure queue-based webhook processing.
/// </summary>
public static class AzureWebhooksExtensions
{
    /// <summary>
    /// Adds Azure Storage Queue-based webhook retry processing.
    /// Use this instead of AddWebhookRetryProcessing() for production multi-instance deployments.
    /// </summary>
    /// <remarks>
    /// This registers:
    /// - Azure Storage Queue client for webhook retries
    /// - QueueWebhookRetryProcessor as IWebhookRetryProcessor
    /// - Disables the in-process retry processor
    ///
    /// You'll need an Azure Function with a Queue trigger to process retries:
    /// <code>
    /// [Function("ProcessWebhookRetries")]
    /// public async Task Run(
    ///     [QueueTrigger("oluso-webhook-retries")] string message,
    ///     FunctionContext context)
    /// {
    ///     var retryMessage = JsonSerializer.Deserialize&lt;WebhookRetryMessage&gt;(message);
    ///     var dispatcher = context.InstanceServices.GetRequiredService&lt;IWebhookDispatcher&gt;();
    ///     await dispatcher.RetryDeliveryAsync(retryMessage.DeliveryId);
    /// }
    /// </code>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Using connection string
    /// builder.Services.AddOlusoWebhooks()
    ///     .AddQueueBasedWebhookRetries(opts =>
    ///     {
    ///         opts.ConnectionString = configuration["AzureStorage:ConnectionString"];
    ///     });
    ///
    /// // Using Managed Identity
    /// builder.Services.AddOlusoWebhooks()
    ///     .AddQueueBasedWebhookRetries(opts =>
    ///     {
    ///         opts.QueueServiceUri = new Uri("https://yourstorage.queue.core.windows.net");
    ///     });
    /// </code>
    /// </example>
    public static IServiceCollection AddQueueBasedWebhookRetries(
        this IServiceCollection services,
        Action<QueueWebhookOptions>? configure = null)
    {
        var options = new QueueWebhookOptions();
        configure?.Invoke(options);

        services.Configure<QueueWebhookOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.QueueServiceUri = options.QueueServiceUri;
            o.QueueName = options.QueueName;
            o.BatchSize = options.BatchSize;
            o.CreateQueueIfNotExists = options.CreateQueueIfNotExists;
        });

        // Register the queue client
        services.AddSingleton(sp =>
        {
            QueueClient client;

            if (!string.IsNullOrEmpty(options.ConnectionString))
            {
                client = new QueueClient(options.ConnectionString, options.QueueName);
            }
            else if (options.QueueServiceUri != null)
            {
                var queueUri = new Uri(options.QueueServiceUri, options.QueueName);
                client = new QueueClient(queueUri, new DefaultAzureCredential());
            }
            else
            {
                throw new InvalidOperationException(
                    "Either ConnectionString or QueueServiceUri must be configured for Azure Queue webhook processing.");
            }

            if (options.CreateQueueIfNotExists)
            {
                client.CreateIfNotExists();
            }

            return client;
        });

        // Register the queue-based processor
        services.AddSingleton<QueueWebhookRetryProcessor>();
        services.AddSingleton<IWebhookRetryProcessor>(sp => sp.GetRequiredService<QueueWebhookRetryProcessor>());

        // Disable in-process retries
        services.DisableInProcessWebhookRetries();

        return services;
    }

    /// <summary>
    /// Adds Azure queue-based webhook retry processing to the OlusoBuilder.
    /// </summary>
    public static OlusoBuilder AddQueueBasedWebhookRetries(
        this OlusoBuilder builder,
        Action<QueueWebhookOptions>? configure = null)
    {
        builder.Services.AddQueueBasedWebhookRetries(configure);
        return builder;
    }
}
