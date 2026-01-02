using Oluso.Enterprise.Scim.Entities;

namespace Oluso.Enterprise.Scim.Services;

/// <summary>
/// Provides access to the current SCIM client context (set by authentication middleware)
/// </summary>
public interface IScimContextAccessor
{
    /// <summary>
    /// The authenticated SCIM client making the current request
    /// </summary>
    ScimClient? Client { get; set; }
}

/// <summary>
/// Default implementation using AsyncLocal for request-scoped storage
/// </summary>
public class ScimContextAccessor : IScimContextAccessor
{
    private static readonly AsyncLocal<ScimClientHolder> _clientHolder = new();

    public ScimClient? Client
    {
        get => _clientHolder.Value?.Client;
        set
        {
            var holder = _clientHolder.Value;
            if (holder != null)
            {
                holder.Client = null;
            }

            if (value != null)
            {
                _clientHolder.Value = new ScimClientHolder { Client = value };
            }
        }
    }

    private class ScimClientHolder
    {
        public ScimClient? Client;
    }
}
