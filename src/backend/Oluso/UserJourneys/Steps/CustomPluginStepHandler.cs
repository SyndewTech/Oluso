using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Executes custom plugin steps using the plugin executor.
/// Supports both .NET managed plugins and WASM plugins via Extism.
/// Plugins are loaded from the plugin store (database, filesystem, or blob storage).
/// </summary>
public class CustomPluginStepHandler : IStepHandler
{
    public string StepType => "custom_plugin";

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        var name = StepConfigurationHelpers.GetString(configuration, "pluginName");
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(StepConfigurationValidationResult.Invalid("Plugin name is required"));
        }
        return Task.FromResult(StepConfigurationValidationResult.Valid());
    }

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var pluginExecutor = context.ServiceProvider.GetService<IPluginExecutor>();
        var managedRegistry = context.ServiceProvider.GetService<IManagedPluginRegistry>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<CustomPluginStepHandler>>();

        // Get the plugin name from step configuration or context
        var pluginName = context.PluginName ?? context.GetConfig<string>("pluginName", null);
        var entryPoint = context.GetConfig<string>("entryPoint", "execute");
        // Plugin-specific config object passed through to the plugin so it
        // reads its own settings explicitly rather than picking through
        // journey state.
        var pluginConfig = context.GetConfig<Dictionary<string, object>?>("config", null);

        if (string.IsNullOrEmpty(pluginName))
        {
            logger.LogError("CustomPlugin step missing required 'pluginName' configuration");
            return StepHandlerResult.Fail("plugin_config_error", "Plugin name not specified");
        }

        logger.LogDebug("Executing custom plugin {PluginName}.{EntryPoint}", pluginName, entryPoint);

        // Try managed (.NET) plugins first via registry
        var managedPlugin = managedRegistry?.Get(pluginName);
        if (managedPlugin != null)
        {
            return await ExecuteManagedPluginAsync(managedPlugin, entryPoint!, context, pluginConfig, logger, cancellationToken);
        }

        // Try WASM plugins via executor
        if (pluginExecutor != null)
        {
            return await ExecuteWasmPluginAsync(pluginExecutor, pluginName, entryPoint!, context, pluginConfig, logger, cancellationToken);
        }

        logger.LogError("No plugin executor or registry available");
        return StepHandlerResult.Fail("plugin_unavailable", "Plugin system not configured");
    }

    private async Task<StepHandlerResult> ExecuteManagedPluginAsync(
        IManagedPlugin plugin,
        string entryPoint,
        StepExecutionContext context,
        Dictionary<string, object>? pluginConfig,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Redact credential-bearing keys before they cross into the
            // plugin sandbox. Plugins are extension code we don't fully
            // trust — they should never see passwords or OTPs even if a
            // poorly-designed flow stages them in journey state.
            var pluginContext = new PluginExecutionContext
            {
                UserId = context.UserId,
                TenantId = context.TenantId,
                Input = SensitiveInputs.RedactCopy(context.UserInput),
                JourneyData = SensitiveInputs.RedactCopy(context.JourneyData),
                Config = pluginConfig
            };

            var result = await plugin.ExecuteAsync(entryPoint, pluginContext, cancellationToken);

            logger.LogDebug("Managed plugin {PluginName} returned success={Success}", plugin.Name, result.Success);

            return MapPluginResult(result, context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing managed plugin {PluginName}", plugin.Name);
            return StepHandlerResult.Fail("plugin_error", $"Plugin execution failed: {ex.Message}");
        }
    }

    private async Task<StepHandlerResult> ExecuteWasmPluginAsync(
        IPluginExecutor executor,
        string pluginName,
        string entryPoint,
        StepExecutionContext context,
        Dictionary<string, object>? pluginConfig,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Redact credential-bearing keys before they cross into the
            // plugin sandbox. Plugins are extension code we don't fully
            // trust — they should never see passwords or OTPs even if a
            // poorly-designed flow stages them in journey state.
            var pluginContext = new PluginExecutionContext
            {
                UserId = context.UserId,
                TenantId = context.TenantId,
                Input = SensitiveInputs.RedactCopy(context.UserInput),
                JourneyData = SensitiveInputs.RedactCopy(context.JourneyData),
                Config = pluginConfig
            };

            var result = await executor.ExecuteAsync(pluginName, entryPoint, pluginContext, cancellationToken);

            logger.LogDebug("WASM plugin {PluginName} returned success={Success}, action={Action}",
                pluginName, result.Success, result.Action);

            return MapPluginResult(result, context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing WASM plugin {PluginName}", pluginName);
            return StepHandlerResult.Fail("plugin_error", $"Plugin execution failed: {ex.Message}");
        }
    }

    private static StepHandlerResult MapPluginResult(PluginExecutionResult result, StepExecutionContext context)
    {
        if (!result.Success)
        {
            return StepHandlerResult.Fail("plugin_error", result.Error ?? "Plugin execution failed");
        }

        return result.Action switch
        {
            PluginAction.Continue => StepHandlerResult.Success(result.Output),
            PluginAction.Complete => StepHandlerResult.Success(result.Output),
            // The plugin describes its form like a dynamic_form step's settings.
            PluginAction.RequireInput => StepHandlerResult.ShowUi("Journey/_DynamicForm",
                DynamicFormStepHandler.FromDefinition(
                    System.Text.Json.JsonSerializer.SerializeToElement(result.Output ?? new Dictionary<string, object>()),
                    context.ResolvedLocale, context.UserInput)),
            PluginAction.Branch => result.Output?.TryGetValue("branchId", out var branchId) == true
                ? StepHandlerResult.Branch(branchId?.ToString() ?? "default", result.Output)
                : StepHandlerResult.Success(result.Output),
            PluginAction.Fail => StepHandlerResult.Fail("plugin_fail", result.Error ?? "Plugin indicated failure"),
            _ => StepHandlerResult.Success(result.Output)
        };
    }
}
