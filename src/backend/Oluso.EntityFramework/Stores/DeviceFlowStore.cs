using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IDeviceFlowStore
/// </summary>
public class DeviceFlowStore : IDeviceFlowStore
{
    private readonly IOlusoDbContext _context;

    public DeviceFlowStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task StoreDeviceAuthorizationAsync(
        string deviceCode,
        string userCode,
        DeviceFlowCode data,
        CancellationToken cancellationToken = default)
    {
        data.DeviceCode = deviceCode;
        data.UserCode = userCode;

        _context.DeviceFlowCodes.Add(data);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<DeviceFlowCode?> FindByUserCodeAsync(
        string userCode,
        CancellationToken cancellationToken = default)
    {
        return await _context.DeviceFlowCodes
            .FirstOrDefaultAsync(d => d.UserCode == userCode, cancellationToken);
    }

    public async Task<DeviceFlowCode?> FindByDeviceCodeAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        return await _context.DeviceFlowCodes
            .FirstOrDefaultAsync(d => d.DeviceCode == deviceCode, cancellationToken);
    }

    public async Task UpdateByUserCodeAsync(
        string userCode,
        DeviceFlowCode data,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.DeviceFlowCodes
            .FirstOrDefaultAsync(d => d.UserCode == userCode, cancellationToken);

        if (existing != null)
        {
            // Authorization state is stored in Data (serialized)
            existing.SubjectId = data.SubjectId;
            existing.SessionId = data.SessionId;
            existing.Data = data.Data;

            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RemoveByDeviceCodeAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        var code = await _context.DeviceFlowCodes
            .FirstOrDefaultAsync(d => d.DeviceCode == deviceCode, cancellationToken);

        if (code != null)
        {
            _context.DeviceFlowCodes.Remove(code);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
