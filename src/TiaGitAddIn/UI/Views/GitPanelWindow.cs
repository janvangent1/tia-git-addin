using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TiaGitAddIn.UI.ViewModels;

namespace TiaGitAddIn.UI.Views
{
    public sealed class GitPanelWindow : Window
    {
        private readonly MainViewModel viewModel;

        public GitPanelWindow(MainViewModel viewModel)
        {
            this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = viewModel;
            Title = "TIA Git";
            Width = 980;
            Height = 660;
            MinWidth = 760;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Content = BuildContent(viewModel);
            Loaded += OnLoaded;
        }

        private static FrameworkElement BuildContent(MainViewModel vm)
        {
            Grid root = new();
            root.Children.Add(BuildShell(vm));
            root.Children.Add(BuildBusyOverlay());
            return root;
        }

        private static FrameworkElement BuildShell(MainViewModel vm)
        {
            DockPanel shell = new()

            {
                Margin = new Thickness(12)
            };

            shell.Children.Add(BuildHeader());
            shell.Children.Add(BuildTabs(vm));
            return shell;
        }

        private static FrameworkElement BuildHeader()
        {
            StackPanel header = new()

            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel.SetDock(header, Dock.Top);

            TextBlock repository = new()

            {
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            repository.SetBinding(TextBlock.TextProperty, new Binding("RepositoryPath")
            {
                StringFormat = "Repository: {0}"
            });

            TextBlock branch = new();
            branch.SetBinding(TextBlock.TextProperty, new Binding("Status.CurrentBranch")
            {
                StringFormat = "Branch: {0}"
            });

            TextBlock tracking = new();
            tracking.SetBinding(TextBlock.TextProperty, new Binding("Status.TrackingSummary"));

            header.Children.Add(repository);
            header.Children.Add(branch);
            header.Children.Add(tracking);

            StackPanel hmiActions = new()
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            hmiActions.Children.Add(HmiButton(
                "Export HMI",
                "Hmi.ExportCommand",
                "Write screens, tags, alarms, and scripts from the open Unified or WinCC Comfort device (panel or runtime) to HmiExport/<device>/."));
            hmiActions.Children.Add(HmiButton(
                "Import HMI",
                "Hmi.ImportCommand",
                "Apply HmiExport/<device>/ back onto the open Unified or WinCC Comfort device (panel or runtime) after a checkout."));
            TextBlock hmiMessage = new()
            {
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.DimGray,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            hmiMessage.SetBinding(TextBlock.TextProperty, new Binding("Hmi.LastOperationMessage"));
            hmiActions.Children.Add(hmiMessage);
            header.Children.Add(hmiActions);
            return header;
        }

        private static Button HmiButton(string content, string commandPath, string toolTip)
        {
            Button button = new()
            {
                Content = content,
                Padding = new Thickness(10, 2, 10, 2),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = toolTip
            };
            button.SetBinding(Button.CommandProperty, new Binding(commandPath));
            return button;
        }

        private static FrameworkElement BuildTabs(MainViewModel vm)
        {
            TabControl tabs = new TabControl
            {
                Items =
                {
                    new TabItem
                    {
                        Header = "Status",
                        Content = new StatusView { DataContext = vm.Status }
                    },
                    new TabItem
                    {
                        Header = "Commit",
                        Content = new CommitView { DataContext = vm.Commit }
                    },
                    new TabItem
                    {
                        Header = "Branch",
                        Content = new BranchView { DataContext = vm.Branch }
                    },
                    new TabItem
                    {
                        Header = "History",
                        Content = new HistoryView { DataContext = vm.History }
                    },
                    new TabItem
                    {
                        Header = "Diff",
                        Content = new DiffView { DataContext = vm.Diff }
                    },
                    new TabItem
                    {
                        Header = "Settings",
                        Content = new SettingsView { DataContext = vm.Settings }
                    }
                }
            };
            tabs.SetBinding(TabControl.SelectedIndexProperty, new Binding(nameof(MainViewModel.SelectedTabIndex))
            {
                Mode = BindingMode.TwoWay
            });
            tabs.SelectionChanged += OnTabSelectionChanged;
            return tabs;
        }

        private static void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, sender))
            {
                return;
            }

            if (sender is not TabControl tabs || tabs.SelectedItem is not TabItem item)
            {
                return;
            }

            if (item.Content is HistoryView historyView && historyView.DataContext is HistoryViewModel history)
            {
                _ = history.RefreshAsync();
            }
            else if (item.Content is BranchView branchView && branchView.DataContext is BranchViewModel branch)
            {
                _ = branch.RefreshAsync();
            }
        }

        private static FrameworkElement BuildBusyOverlay()
        {
            Grid overlay = new()

            {
                Background = new SolidColorBrush(Color.FromArgb(160, 20, 20, 20)),
                IsHitTestVisible = true
            };
            overlay.SetBinding(UIElement.VisibilityProperty, new Binding("IsBusy")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            Border card = new()

            {
                Background = new SolidColorBrush(Color.FromArgb(245, 30, 30, 30)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(40, 28, 40, 28),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 280
            };

            StackPanel panel = new()

            {
                HorizontalAlignment = HorizontalAlignment.Center
            };

            ProgressBar progress = new()

            {
                IsIndeterminate = true,
                Width = 220,
                Height = 4,
                Margin = new Thickness(0, 0, 0, 16)
            };

            TextBlock message = new()

            {
                Foreground = Brushes.White,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460
            };
            message.SetBinding(TextBlock.TextProperty, new Binding("BusyMessage"));

            Button cancel = new()

            {
                Content = "Cancel",
                MinWidth = 90,
                Padding = new Thickness(20, 6, 20, 6),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            cancel.SetBinding(Button.CommandProperty, new Binding("CancelCommand"));

            panel.Children.Add(progress);
            panel.Children.Add(message);
            panel.Children.Add(cancel);
            card.Child = panel;
            overlay.Children.Add(card);
            return overlay;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await RefreshOnLoadAsync().ConfigureAwait(true);
        }

        private async Task RefreshOnLoadAsync()
        {
            try
            {
                await viewModel.RefreshOnOpenAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "TIA Git",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
