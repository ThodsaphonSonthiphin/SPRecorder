using Microsoft.Extensions.Configuration;
using SPRecorder.Configuration;
using SPRecorder.Settings;
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

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        IConfiguration cfg = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var initialConfig = AppConfig.Load(cfg);
        var store = new AppConfigStore(settingsPath, initialConfig);

        Application.Run(new TrayApp(store));
    }
}
