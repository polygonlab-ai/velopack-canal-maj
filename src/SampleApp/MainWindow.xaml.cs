using System.Windows;

namespace SampleApp;

/// <summary>Main window: every value and action comes from <see cref="MainViewModel"/>.</summary>
internal sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    // The automatic check starts once the window is visible, in the background: the window never waits for it.
    private void OnLoaded(object sender, RoutedEventArgs e) => _viewModel.StartAutomaticCheck();

    private void OnClosed(object? sender, EventArgs e) => _viewModel.Dispose();
}
