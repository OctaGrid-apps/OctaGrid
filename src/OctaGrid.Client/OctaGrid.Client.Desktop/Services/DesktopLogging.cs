using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace OctaGrid.Client.Desktop.Services;

public static class DesktopLogging
{
    public static ILogger Initialize()
    {
        var directory = Directory.CreateDirectory(Helper.LogsDirectory);
        var logFilePath = Path.Combine(directory.FullName, "octagrid-.log");

        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .MinimumLevel.Override("Avalonia", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: logFilePath,
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: false,
                buffered: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1),
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Logger = logger;

        AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
        {
            Log.CloseAndFlush();
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Unhandled AppDomain exception");
            }
            else
            {
                Log.Fatal("Unhandled AppDomain exception: {ExceptionObject}", args.ExceptionObject);
            }
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        return logger;
    }

    public static IServiceCollection AddDesktopLogging(this IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        return services;
    }
}
