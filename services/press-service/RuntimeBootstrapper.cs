namespace Press.Service;

using Press.Service.Application;

internal sealed class RuntimeBootstrapper(MachineRuntime runtime) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => runtime.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => runtime.DisposeAsync().AsTask();
}
