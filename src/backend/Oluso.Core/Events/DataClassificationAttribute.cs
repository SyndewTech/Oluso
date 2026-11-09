namespace Oluso.Core.Events;

/// <summary>
/// How confidential an event, or a property of one, is.
/// </summary>
public enum DataSensitivity
{
    /// <summary>No classification declared.</summary>
    Unclassified = 0,

    /// <summary>Safe to send outside the platform: opaque identifiers, statuses, timestamps.</summary>
    Internal = 1,

    /// <summary>
    /// Confidential. On an event: subscribing to it, or authoring automations on it, needs
    /// <see cref="DataClassificationAttribute.RequiredPermission"/>, and every property of it is
    /// withheld from outbound deliveries unless marked <see cref="Internal"/>. On a property:
    /// withheld from outbound deliveries unless the receiver is authorised for sensitive data.
    /// </summary>
    Restricted = 2,
}

/// <summary>
/// Classifies the data an event carries. On an event class (inherited by subclasses, so a module can
/// classify its event base once) it marks the event restricted and names the permission an
/// administrator needs to subscribe to it or automate on it. On a property it overrides the event's
/// default: <see cref="DataSensitivity.Internal"/> releases an identifier for outbound use,
/// <see cref="DataSensitivity.Restricted"/> withholds a property of an otherwise unrestricted event.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DataClassificationAttribute : Attribute
{
    public DataClassificationAttribute(DataSensitivity sensitivity) => Sensitivity = sensitivity;

    public DataSensitivity Sensitivity { get; }

    /// <summary>
    /// Permission an administrator must hold to subscribe an endpoint to the event, author an
    /// automation on it, or authorise either to receive its sensitive properties.
    /// </summary>
    public string? RequiredPermission { get; set; }

    /// <summary>Why the data is restricted, shown to administrators.</summary>
    public string? Purpose { get; set; }
}
