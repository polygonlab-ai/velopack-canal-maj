using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using PolygonLab.UpdateChannel;

namespace SampleApp;

/// <summary>State shown by the main window, refreshed from <see cref="UpdateService.StatusChanged"/>.</summary>
internal sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly UpdateService _updates;
    private readonly CancellationTokenSource _closing = new();
    private UpdateStatus _status;

    public MainViewModel(UpdateService updates)
    {
        _updates = updates;
        _status = updates.Status;
        _updates.StatusChanged += OnStatusChanged;
        CheckCommand = new RelayCommand(Check, () => !_status.IsBusy);
        RestartCommand = new RelayCommand(Restart, () => _status.CanRestartToUpdate);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string InstalledVersion => _status.CurrentVersion ?? "not installed (development build)";

    public string Channel => _status.Channel;

    public string FeedUrl => _updates.Options.FeedUrl.ToString();

    public string StatusText => _status.Message;

    public int Progress => _status.Progress ?? 0;

    public bool IsDownloading => _status.State == UpdateState.Downloading;

    public bool CanRestart => _status.CanRestartToUpdate;

    public RelayCommand CheckCommand { get; }

    public RelayCommand RestartCommand { get; }

    /// <summary>Starts the startup check (if enabled in appsettings.json) without waiting for it.</summary>
    public void StartAutomaticCheck() => _ = _updates.StartupCheckAsync(_closing.Token);

    public void Dispose()
    {
        _updates.StatusChanged -= OnStatusChanged;
        _closing.Cancel();
        _closing.Dispose();
    }

    private async void Check()
    {
        try
        {
            await _updates.CheckAndDownloadAsync(_closing.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The window is closing: nothing to show.
        }
    }

    private void Restart()
    {
        // Save the user's work here before restarting: the process exits at once.
        _updates.RestartToUpdate();
    }

    private void OnStatusChanged(object? sender, UpdateStatus status)
    {
        // The service reports from a background thread; WPF bindings must be updated on the UI thread.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Apply(status);
        }
        else
        {
            dispatcher.BeginInvoke(() => Apply(status));
        }
    }

    private void Apply(UpdateStatus status)
    {
        _status = status;
        OnPropertyChanged(nameof(InstalledVersion));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(CanRestart));
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
