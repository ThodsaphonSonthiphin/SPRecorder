using Microsoft.Extensions.Configuration;
using SPRecorder.Configuration;
using SPRecorder.Tray;

namespace SPRecorder;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, "Global\\SPRecorder.SingleInstance", out bool createdNew);
        if (!createdNew) return;

        ApplicationConfiguration.Initialize();

        IConfiguration cfg = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var appConfig = AppConfig.Load(cfg);

        Application.Run(new TrayApp(appConfig));
    }
}
