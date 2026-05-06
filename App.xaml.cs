using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace TaskbarOverlay;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? _mutex;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        const string appName = "TaskbarOverlay_SingleInstanceMutex";
        bool createdNew;

        _mutex = new Mutex(true, appName, out createdNew);

        if (!createdNew)
        {
            // App is already running
            MessageBox.Show("TaskbarOverlay is already running.", "TaskbarOverlay", MessageBoxButton.OK, MessageBoxImage.Information);
            Current.Shutdown();
        }
    }

    private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Debug.WriteLine($"Unhandled exception: {e.Exception.Message}");
        // Log the error securely and prevent a crash if possible
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mutex != null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
