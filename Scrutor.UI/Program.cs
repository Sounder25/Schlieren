using Avalonia;
using System;
using System.IO;

namespace Scrutor.UI;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Surface unhandled exceptions so UI crashes are debuggable (WER alone hides the message).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                var msg = e.ExceptionObject?.ToString() ?? "(null)";
                File.WriteAllText(
                    Path.Combine(AppContext.BaseDirectory, "last_crash.txt"),
                    msg);
                File.WriteAllText(@"C:\projects\Scrutor\ui_crash.txt", msg);
            }
            catch { /* best effort */ }
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
