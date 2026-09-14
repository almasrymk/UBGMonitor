namespace ClientAgent.Service.Dispatch;

public sealed class DispatcherWorker : BackgroundService
{
    private readonly EventDispatcher _dispatcher;
    private readonly ILogger<DispatcherWorker> _logger;

    public DispatcherWorker(EventDispatcher dispatcher, ILogger<DispatcherWorker> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _dispatcher.DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Event dispatcher cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
