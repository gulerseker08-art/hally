using Hangfire;
using Hangfire.PostgreSql;

namespace Hally.Api.Features.Notifications;

public static class JobScheduling
{
    /// <summary>Hangfire with PostgreSQL storage. Disabled with <c>Jobs:Enabled=false</c> (tests call the jobs directly).</summary>
    public static IServiceCollection AddHallyJobs(this IServiceCollection services, IConfiguration config, string connectionString)
    {
        services.AddScoped<NotificationPublisher>();
        services.AddScoped<ReminderDispatcher>();
        services.AddScoped<EveningPrepJob>();
        services.AddScoped<RetentionJob>();
        if (!config.GetValue("Jobs:Enabled", true))
        {
            return services;
        }

        services.AddHangfire(h => h
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString), new PostgreSqlStorageOptions { SchemaName = "hangfire" }));
        services.AddHangfireServer(o => o.WorkerCount = Math.Max(2, Environment.ProcessorCount));
        return services;
    }

    public static void UseHallyJobs(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Jobs:Enabled", true))
        {
            return;
        }

        var jobs = app.Services.GetRequiredService<IRecurringJobManager>();
        jobs.AddOrUpdate<ReminderDispatcher>("reminders", j => j.RunAsync(CancellationToken.None), Cron.Minutely());
        jobs.AddOrUpdate<EveningPrepJob>("evening-prep", j => j.RunAsync(CancellationToken.None), "*/15 * * * *");
        jobs.AddOrUpdate<RetentionJob>("retention", j => j.RunAsync(CancellationToken.None), "30 3 * * *");
    }
}
