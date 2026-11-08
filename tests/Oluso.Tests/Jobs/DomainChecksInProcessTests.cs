using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Jobs;
using Xunit;

namespace Oluso.Tests.Jobs;

/// <summary>
/// Oluso:RunDomainChecksInProcess lets only one of several instances run the domain checks,
/// which aren't designed to run concurrently.
/// </summary>
public class DomainChecksInProcessTests
{
    public static TheoryData<string> Jobs => new() { nameof(DomainVerificationBackgroundService), nameof(DomainRevalidationBackgroundService) };

    [Theory]
    [MemberData(nameof(Jobs))]
    public async Task Off_TheJobEndsWithoutTouchingAnyService(string job)
    {
        var scopeFactory = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var service = Create(job, scopeFactory.Object, runInProcess: false);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        service.ExecuteTask.IsCompletedSuccessfully.Should().BeTrue();
        scopeFactory.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Jobs))]
    public async Task On_TheJobRuns(string job)
    {
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope())
            .Callback(() => ran.TrySetResult())
            .Throws(new InvalidOperationException("stop here"));
        var service = Create(job, scopeFactory.Object, runInProcess: true);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
            service.ExecuteTask!.IsCompleted.Should().BeFalse("the job keeps running on its schedule");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void OnByDefault() => new OlusoOptions().RunDomainChecksInProcess.Should().BeTrue();

    private static BackgroundService Create(string job, IServiceScopeFactory scopeFactory, bool runInProcess)
    {
        var options = Options.Create(new OlusoOptions { RunDomainChecksInProcess = runInProcess });
        return job switch
        {
            nameof(DomainVerificationBackgroundService) => new DomainVerificationBackgroundService(
                scopeFactory, NullLogger<DomainVerificationBackgroundService>.Instance, options),
            _ => new DomainRevalidationBackgroundService(
                scopeFactory, NullLogger<DomainRevalidationBackgroundService>.Instance, options),
        };
    }
}
