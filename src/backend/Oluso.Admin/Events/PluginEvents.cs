using Oluso.Core.Events;

namespace Oluso.Admin.Events;

public class PluginUploadedEvent : AdminActionEvent
{
    public override string EventType => "PluginUploaded";
    public override string ResourceType => "Plugin";
    public string? PluginType { get; init; }
    public long SizeBytes { get; init; }
}

public class PluginUpdatedEvent : AdminActionEvent
{
    public override string EventType => "PluginUpdated";
    public override string ResourceType => "Plugin";
}

public class PluginDeletedEvent : AdminActionEvent
{
    public override string EventType => "PluginDeleted";
    public override string ResourceType => "Plugin";
}

public class PluginHookCreatedEvent : AdminActionEvent
{
    public override string EventType => "PluginHookCreated";
    public override string ResourceType => "PluginHook";
    public string? PluginId { get; init; }
    public string? PluginName { get; init; }
}

public class PluginHookUpdatedEvent : AdminActionEvent
{
    public override string EventType => "PluginHookUpdated";
    public override string ResourceType => "PluginHook";
    public string? PluginId { get; init; }
    public string? PluginName { get; init; }
}

public class PluginHookDeletedEvent : AdminActionEvent
{
    public override string EventType => "PluginHookDeleted";
    public override string ResourceType => "PluginHook";
    public string? PluginId { get; init; }
    public string? PluginName { get; init; }
}

public class PluginNetworkConfigUpdatedEvent : AdminActionEvent
{
    public override string EventType => "PluginNetworkConfigUpdated";
    public override string ResourceType => "PluginNetworkConfig";
    public string? AccessMode { get; init; }
}
