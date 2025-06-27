using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Champ.TCPHealthCheck;

/// <inheritdoc />
public class TCPHealthProbe(HealthCheckService healthCheckService, ILogger<TCPHealthProbe> logger)
    : BackgroundService
{
    private readonly HealthCheckService _healthCheckService =
        healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
    private readonly TcpListener _listener = new(IPAddress.Any, 5999);

    private readonly TimeSpan _period = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Started health check service");
        await Task.Yield();

        using var timer = new PeriodicTimer(_period);

        _listener.Start();

        while (
            !stoppingToken.IsCancellationRequested
            && await timer.WaitForNextTickAsync(stoppingToken)
        )
        {
            // Gather health metrics every second.
            await UpdateHeartbeatAsync(stoppingToken);
        }

        _listener.Stop();
    }

    private async Task UpdateHeartbeatAsync(CancellationToken token)
    {
        try
        {
            var result = await _healthCheckService.CheckHealthAsync(token);
            var isHealthy = result.Status == HealthStatus.Healthy;

            if (!isHealthy)
            {
                _listener.Stop();
                logger.LogWarning("Service is unhealthy. Listener stopped");
                foreach (
                    var entry in result.Entries.Where(e => e.Value.Status != HealthStatus.Healthy)
                )
                {
                    logger.LogWarning(
                        "Health check {Name} status: {Status}",
                        entry.Key,
                        entry.Value.Status
                    );
                    logger.LogWarning(
                        entry.Value.Exception,
                        "{Description}",
                        entry.Value.Description
                    );
                }
                return;
            }

            _listener.Start();
            while (_listener.Server.IsBound && _listener.Pending())
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                client.Close();
                foreach (var entry in result.Entries)
                {
                    logger.LogInformation(
                        "Health check {Name} status: {Status}",
                        entry.Key,
                        entry.Value.Status
                    );
                }
            }

            logger.LogDebug("Heartbeat check executed");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "An error occurred while checking heartbeat");
        }
    }
    
    /// <inheritdoc />
    public override void Dispose()
    {
        GC.SuppressFinalize(this);
        _listener.Stop();
        base.Dispose();
    }
}
