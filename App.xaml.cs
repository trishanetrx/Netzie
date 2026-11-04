using System;
using System.IO;
using System.Threading;
using System.Windows;
using Serilog;

namespace NetSentry
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _appMutex;
        private static bool _hasHandle = false;

        public App()
        {
            string logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NetSentry", "logs");

            try
            {
                Directory.CreateDirectory(logDirectory);
            }
            catch { }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(logDirectory, "netsentry-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("NetSentry starting up (PID: {ProcessId})", Environment.ProcessId);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Log.Fatal(ex, "AppDomain Unhandled Exception occurred");
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.ExceptionObject?.ToString());
                }
                catch { }
            };

            DispatcherUnhandledException += (s, e) =>
            {
                Log.Error(e.Exception, "Dispatcher Unhandled Exception caught");
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.Exception?.ToString());
                }
                catch { }
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            _appMutex = new Mutex(true, "Global\\NetSentry_SingleInstance_TM_Ventiqo", out _hasHandle);
            if (!_hasHandle)
            {
                Log.Warning("Another instance of NetSentry is already active. Exiting secondary instance.");
                System.Windows.MessageBox.Show("NetSentry is already running in the background or system tray.", "NetSentry", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("NetSentry shutting down (ExitCode: {ExitCode})", e.ApplicationExitCode);
            Log.CloseAndFlush();

            if (_appMutex != null)
            {
                if (_hasHandle)
                {
                    try { _appMutex.ReleaseMutex(); } catch { }
                }
                _appMutex.Dispose();
                _appMutex = null;
            }

            base.OnExit(e);
        }
    }
}
