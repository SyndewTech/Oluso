using System.Text.Json;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.UserJourneys;

// Use alias to resolve ambiguity with UserJourneys.PluginExecutionResult
using HookExecutionResultEnum = Oluso.Core.Domain.Entities.PluginExecutionResult;

namespace Oluso.Core.Plugins;

/// <summary>
/// Default implementation of IPluginHookRuntime that uses IPluginExecutor for WASM execution
/// </summary>
public class PluginHookRuntime : IPluginHookRuntime
{
    private readonly IPluginExecutor _pluginExecutor;
    private readonly IPluginHookStore _hookStore;
    private readonly IPluginExecutionLogger _executionLogger;
    private readonly ILogger<PluginHookRuntime> _logger;

    public PluginHookRuntime(
        IPluginExecutor pluginExecutor,
        IPluginHookStore hookStore,
        IPluginExecutionLogger executionLogger,
        ILogger<PluginHookRuntime> logger)
    {
        _pluginExecutor = pluginExecutor;
        _hookStore = hookStore;
        _executionLogger = executionLogger;
        _logger = logger;
    }

    public async Task<HookExecutionResult<TResponse>> ExecuteHooksAsync<TContext, TResponse>(
        string tenantId,
        string domain,
        string entityType,
        string eventType,
        TContext context,
        CancellationToken cancellationToken = default)
        where TContext : BaseHookContext
        where TResponse : BaseHookResponse, new()
    {
        var hooks = await GetActiveHooksAsync(tenantId, domain, entityType, eventType, cancellationToken);

        if (hooks.Count == 0)
        {
            return HookExecutionResult<TResponse>.Continue(new TResponse());
        }

        var results = new List<SingleHookResult>();
        TResponse currentResponse = new();

        foreach (var hook in hooks.OrderBy(h => h.Order))
        {
            try
            {
                var result = await ExecuteSingleHookAsync<TContext, TResponse>(hook, context, cancellationToken);
                results.Add(result);

                if (result.Result == HookExecutionResultEnum.Error)
                {
                    _logger.LogWarning(
                        "Hook {PluginId}/{FunctionName} failed with error: {Error}",
                        hook.PluginId, hook.FunctionName, result.ErrorMessage);

                    if (hook.FailureMode == PluginFailureMode.Reject)
                    {
                        return HookExecutionResult<TResponse>.Error(result.ErrorMessage ?? "Hook execution failed");
                    }
                    // FailureMode.Continue - skip to next hook
                    continue;
                }

                if (result.Action == "reject")
                {
                    // Parse the response and return rejection
                    return HookExecutionResult<TResponse>.Rejected(currentResponse, results);
                }

                if (result.Action == "skip")
                {
                    // Skip remaining hooks
                    break;
                }

                // Continue - update current response if provided
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error executing hook {PluginId}/{FunctionName}",
                    hook.PluginId, hook.FunctionName);

                if (hook.FailureMode == PluginFailureMode.Reject)
                {
                    return HookExecutionResult<TResponse>.Error(ex.Message);
                }
            }
        }

        return HookExecutionResult<TResponse>.Continue(currentResponse, results);
    }

    public async Task<IReadOnlyList<PluginHookBinding>> GetActiveHooksAsync(
        string tenantId,
        string domain,
        string entityType,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        return await _hookStore.GetActiveHooksAsync(tenantId, domain, entityType, eventType, cancellationToken);
    }

    private async Task<SingleHookResult> ExecuteSingleHookAsync<TContext, TResponse>(
        PluginHookBinding hook,
        TContext context,
        CancellationToken cancellationToken)
        where TContext : BaseHookContext
        where TResponse : BaseHookResponse, new()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Build plugin input
            var pluginInput = new Dictionary<string, object>
            {
                ["tenant_id"] = context.TenantId,
                ["timestamp"] = context.Timestamp.ToString("O"),
                ["execution_id"] = context.ExecutionId,
                ["domain"] = hook.Domain,
                ["context"] = context.GetDomainContext()
            };

            if (context.User != null)
            {
                pluginInput["user"] = new
                {
                    id = context.User.Id,
                    email = context.User.Email,
                    name = context.User.Name,
                    roles = context.User.Roles,
                    permissions = context.User.Permissions
                };
            }

            // Get plugin config (TODO: merge with overrides)
            // pluginInput["config"] = await GetPluginConfigAsync(hook.PluginId, context.TenantId, cancellationToken);

            // Execute via IPluginExecutor
            var pluginContext = new PluginExecutionContext
            {
                UserId = context.User?.Id,
                TenantId = context.TenantId,
                Input = pluginInput
            };

            // Check if plugin is loaded, load if not
            if (!_pluginExecutor.IsPluginLoaded(hook.PluginId))
            {
                _logger.LogDebug("Plugin {PluginId} not loaded, skipping hook execution", hook.PluginId);
                return new SingleHookResult
                {
                    PluginId = hook.PluginId,
                    HookBindingId = hook.Id,
                    FunctionName = hook.FunctionName,
                    DurationMs = (int)stopwatch.ElapsedMilliseconds,
                    Result = HookExecutionResultEnum.Skipped,
                    Action = "skip",
                    ErrorMessage = "Plugin not loaded"
                };
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(hook.TimeoutMs));

            var result = await _pluginExecutor.ExecuteAsync(
                hook.PluginId,
                hook.FunctionName,
                pluginContext,
                cts.Token);

            stopwatch.Stop();

            var hookResult = new SingleHookResult
            {
                PluginId = hook.PluginId,
                HookBindingId = hook.Id,
                FunctionName = hook.FunctionName,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
                Result = result.Success ? HookExecutionResultEnum.Success : HookExecutionResultEnum.Error,
                Action = result.Output != null && result.Output.TryGetValue("action", out var action) ? action?.ToString() ?? "continue" : "continue",
                ErrorMessage = result.Error
            };

            // Log execution
            await _executionLogger.LogExecutionAsync(new PluginExecutionLog
            {
                TenantId = context.TenantId,
                PluginId = hook.PluginId,
                HookBindingId = hook.Id,
                ExecutionId = context.ExecutionId,
                Domain = hook.Domain,
                EventType = hook.EventType,
                EntityType = hook.EntityType,
                DurationMs = hookResult.DurationMs,
                Result = hookResult.Result,
                ResultAction = hookResult.Action,
                ErrorMessage = hookResult.ErrorMessage,
                UserId = context.User?.Id,
                ExecutedAt = DateTime.UtcNow
            }, cancellationToken);

            return hookResult;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout
            stopwatch.Stop();
            return new SingleHookResult
            {
                PluginId = hook.PluginId,
                HookBindingId = hook.Id,
                FunctionName = hook.FunctionName,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
                Result = HookExecutionResultEnum.Timeout,
                Action = "continue",
                ErrorMessage = $"Plugin execution timed out after {hook.TimeoutMs}ms"
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new SingleHookResult
            {
                PluginId = hook.PluginId,
                HookBindingId = hook.Id,
                FunctionName = hook.FunctionName,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
                Result = HookExecutionResultEnum.Error,
                Action = "continue",
                ErrorMessage = ex.Message
            };
        }
    }
}

/// <summary>
/// Store interface for plugin hook bindings
/// </summary>
public interface IPluginHookStore
{
    /// <summary>
    /// Get active hooks for a domain event
    /// </summary>
    Task<IReadOnlyList<PluginHookBinding>> GetActiveHooksAsync(
        string tenantId,
        string domain,
        string entityType,
        string eventType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all hooks for a plugin
    /// </summary>
    Task<IReadOnlyList<PluginHookBinding>> GetHooksByPluginAsync(
        string tenantId,
        string pluginId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a hook binding
    /// </summary>
    Task<PluginHookBinding> CreateAsync(PluginHookBinding binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a hook binding
    /// </summary>
    Task<PluginHookBinding> UpdateAsync(PluginHookBinding binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a hook binding
    /// </summary>
    Task DeleteAsync(string tenantId, string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Logger interface for plugin executions
/// </summary>
public interface IPluginExecutionLogger
{
    /// <summary>
    /// Log a plugin execution
    /// </summary>
    Task LogExecutionAsync(PluginExecutionLog log, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent executions for a plugin
    /// </summary>
    Task<IReadOnlyList<PluginExecutionLog>> GetRecentExecutionsAsync(
        string tenantId,
        string pluginId,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get executions by execution ID (for tracing)
    /// </summary>
    Task<IReadOnlyList<PluginExecutionLog>> GetExecutionsByIdAsync(
        string tenantId,
        string executionId,
        CancellationToken cancellationToken = default);
}
