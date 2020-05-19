using System;
using Avalonia;
using Avalonia.Logging.Serilog;
using VtNetCore.Avalonia.App.ViewModels;
using VtNetCore.Avalonia.App.Views;
using ReactiveUI;
using Avalonia.ReactiveUI;

namespace VtNetCore.Avalonia.App
{
    class Program
    {
        static void Main(string[] args)
        {
            BuildAvaloniaApp().Start<MainWindow>(() => new MainWindowViewModel());
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .UseReactiveUI()
                .LogToDebug();
    }
}
