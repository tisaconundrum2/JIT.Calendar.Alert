using JustInTimeAlerts.Services;

namespace JustInTimeAlerts;

public partial class App : Application
{
    private readonly DebugLogService _log;

    public App(DebugLogService log)
    {
        _log = log;

        // Replay any crash log written by the previous run before it was killed.
        var previousCrash = log.ConsumePreviousCrashLog();
        if (previousCrash is not null)
            log.Log($"[Previous session crash log]{Environment.NewLine}{previousCrash}");

        // Unhandled exceptions thrown on non-UI / background threads.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _log.LogException("AppDomain.UnhandledException", (Exception)e.ExceptionObject);

        // Fire-and-forget Tasks whose exceptions were never awaited.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _log.LogException("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved(); // prevent the process from being torn down on .NET 6+
        };

        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}