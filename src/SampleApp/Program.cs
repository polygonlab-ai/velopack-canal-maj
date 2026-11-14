using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PolygonLab.UpdateChannel;
using Velopack;

namespace SampleApp;

/// <summary>Entry point. Replaces the Main method that WPF would generate from App.xaml.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Logging only goes to the debugger output (Visual Studio, DebugView): no file is imposed.
        // Creating the factory is cheap and has no side effect, so it may come before Velopack.
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Debug));

        // MUST run first, before any window, file or network access.
        // Velopack starts the application with special arguments while it installs, updates or uninstalls it
        // (hooks). Run() handles them and exits the process at once when needed; in a normal start it returns
        // immediately. It also applies an update downloaded earlier, if any (SetAutoApplyOnStartup, on by default).
        // vpk pack checks that this call exists in the main executable.
        VelopackApp.Build()
            .SetArgs(args)
            .SetLogger(new VelopackLoggerAdapter(loggerFactory.CreateLogger("Velopack")))
            .Run();

        UpdateChannelOptions options;
        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .Build();
            options = UpdateChannelOptions.FromConfiguration(configuration);
        }
        catch (UpdateChannelConfigurationException ex)
        {
            System.Windows.MessageBox.Show(
                string.Join(Environment.NewLine, ex.Errors),
                "SampleApp: invalid appsettings.json",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return 1;
        }

        using var updates = new UpdateService(new VelopackUpdateEngine(options), options, loggerFactory.CreateLogger<UpdateService>());
        var app = new App();
        app.InitializeComponent();
        return app.Run(new MainWindow(new MainViewModel(updates)));
    }
}
