using Microsoft.UI.Xaml;
using System;

namespace FigPin
{
    public partial class App : Application
    {
        private Window? m_window;

        public App()
        {
            Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
            this.UnhandledException += (sender, e) =>
            {
                try
                {
                    string logDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".figpin");
                    System.IO.Directory.CreateDirectory(logDir);
                    string crashFile = System.IO.Path.Combine(logDir, "crash.log");
                    System.IO.File.AppendAllText(crashFile, $"[{DateTime.Now}] UNHANDLED EXCEPTION:\n{e.Message}\n{e.Exception}\n{e.Exception?.StackTrace}\n\n");
                }
                catch { }
            };

            this.InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            m_window.Activate();
        }
    }
}
