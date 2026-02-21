using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PushDataToGitHub.App.ViewModels;

namespace PushDataToGitHub.App;

public partial class MainWindow : System.Windows.Window
{
    private INotifyCollectionChanged? _attachedLogs;
    private INotifyCollectionChanged? _attachedGitHubLogs;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += MainWindow_OnDataContextChanged;
        Unloaded += MainWindow_OnUnloaded;
    }

    private void FolderDropZone_OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void FolderDropZone_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return;
        }

        string[]? droppedItems = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
        string? folderPath = droppedItems?.FirstOrDefault(Directory.Exists);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        viewModel.ApplyDroppedFolder(folderPath);
    }

    private void PersonalAccessTokenBox_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (sender is not PasswordBox passwordBox)
        {
            return;
        }

        viewModel.UpdatePersonalAccessToken(passwordBox.Password);
    }

    private void MainWindow_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_attachedLogs is not null)
        {
            _attachedLogs.CollectionChanged -= Logs_OnCollectionChanged;
            _attachedLogs = null;
        }

        if (_attachedGitHubLogs is not null)
        {
            _attachedGitHubLogs.CollectionChanged -= GitHubLogs_OnCollectionChanged;
            _attachedGitHubLogs = null;
        }

        if (e.NewValue is MainViewModel viewModel)
        {
            _attachedLogs = viewModel.Logs;
            _attachedLogs.CollectionChanged += Logs_OnCollectionChanged;

            _attachedGitHubLogs = viewModel.GitHubLogs;
            _attachedGitHubLogs.CollectionChanged += GitHubLogs_OnCollectionChanged;
        }
    }

    private void MainWindow_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_attachedLogs is not null)
        {
            _attachedLogs.CollectionChanged -= Logs_OnCollectionChanged;
            _attachedLogs = null;
        }

        if (_attachedGitHubLogs is not null)
        {
            _attachedGitHubLogs.CollectionChanged -= GitHubLogs_OnCollectionChanged;
            _attachedGitHubLogs = null;
        }
    }

    private void Logs_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null || e.NewItems.Count == 0)
        {
            return;
        }

        object latestItem = e.NewItems[e.NewItems.Count - 1]!;
        Dispatcher.BeginInvoke(() =>
        {
            if (ExecutionLogsListView.Items.Count == 0)
            {
                return;
            }

            ExecutionLogsListView.ScrollIntoView(latestItem);
        }, DispatcherPriority.Background);
    }

    private void GitHubLogs_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null || e.NewItems.Count == 0)
        {
            return;
        }

        object latestItem = e.NewItems[e.NewItems.Count - 1]!;
        Dispatcher.BeginInvoke(() =>
        {
            if (GitHubRawLogsListBox.Items.Count == 0)
            {
                return;
            }

            GitHubRawLogsListBox.ScrollIntoView(latestItem);
        }, DispatcherPriority.Background);
    }
}
