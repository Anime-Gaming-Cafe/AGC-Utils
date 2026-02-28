using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace AGC_Management.Services;

public class BotReadinessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Check Discord readiness
        var discordReady = DiscordBotService.IsReady && DiscordBotService.Client != null;

        // Check DB connectivity (basic)
        try
        {
            var ds = CurrentApplication.ServiceProvider?.GetService(typeof(NpgsqlDataSource)) as NpgsqlDataSource;
            if (ds == null)
                return Task.FromResult(HealthCheckResult.Unhealthy("NpgsqlDataSource not available"));
            using var cmd = ds.CreateCommand("SELECT 1");
            var _ = cmd.ExecuteScalar();
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Database not available", ex));
        }

        if (!discordReady)
            return Task.FromResult(HealthCheckResult.Unhealthy("Discord client not ready"));

        return Task.FromResult(HealthCheckResult.Healthy("Ready"));
    }
}
