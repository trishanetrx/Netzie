using System;
using System.IO;
using System.Windows;

namespace NetSentry
{
    public partial class App : System.Windows.Application
    {
        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.ExceptionObject?.ToString());
                }
                catch { }
            };

            DispatcherUnhandledException += (s, e) =>
            {
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.Exception?.ToString());
                }
                catch { }
            };
        }
    }
}
