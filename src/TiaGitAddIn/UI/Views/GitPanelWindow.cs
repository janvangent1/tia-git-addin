using System;
using System.ComponentModel;
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
        private TransferStatusWindow? transferWindow;
        private bool panelClosing;

        public GitPanelWindow(MainViewModel viewModel)
        {
            this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            PanelTheme.Apply(this);
            viewModel.Status.Confirm = Confirm;
            viewModel.Hmi.Confirm = Confirm;
            viewModel.Hmi.ShowError = ShowError;
            viewModel.Hmi.PropertyChanged += OnHmiPropertyChanged;
            DataContext = viewModel;
            Title = "TIA Git";
            FontSize = 14;
            Background = PanelTheme.SurfaceBrush();
            Foreground = PanelTheme.InkBrush();
            MinWidth = 900;
            MinHeight = 560;
            FitToWorkArea();
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Content = BuildContent(viewModel);
            Loaded += OnLoaded;
        }

        private void FitToWorkArea()
        {
            Rect work = SystemParameters.WorkArea;
            double width = Math.Min(1500, Math.Max(1180, work.Width * 0.86));
            double height = Math.Min(940, Math.Max(760, work.Height * 0.86));
            if (width > work.Width - 32)
            {
                width = Math.Max(800, work.Width - 32);
            }

            if (height > work.Height - 32)
            {
                height = Math.Max(560, work.Height - 32);
            }

            Width = width;
            Height = height;
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

        protected override void OnClosing(CancelEventArgs e)
        {
            panelClosing = true;
            base.OnClosing(e);
            if (e.Cancel)
            {
                panelClosing = false;
            }
        }

        private bool Confirm(string message)
        {
            return MessageBox.Show(
                this,
                message,
                "TIA Git",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        private void ShowError(string message)
        {
            var window = new Window
            {
                Title = "TIA Git",
                Owner = this,
                Width = 760,
                Height = 520,
                MinWidth = 480,
                MinHeight = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = PanelTheme.SurfaceBrush(),
                Foreground = PanelTheme.InkBrush(),
                FontSize = 14
            };
            var text = new TextBox
            {
                Text = message ?? string.Empty,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(0),
                Background = PanelTheme.SurfaceBrush(),
                Foreground = PanelTheme.InkBrush(),
                FontFamily = new FontFamily("Consolas")
            };
            var copy = new Button
            {
                Content = "Copy",
                Width = 90,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            copy.Click += (_, __) =>
            {
                try
                {
                    Clipboard.SetText(text.Text ?? string.Empty);
                    copy.Content = "Copied";
                }
                catch (Exception)
                {
                    copy.Content = "Copy failed";
                }
            };
            var close = new Button
            {
                Content = "Close",
                Width = 90,
                IsCancel = true
            };
            close.Click += (_, __) => window.Close();
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            buttons.Children.Add(copy);
            buttons.Children.Add(close);
            var layout = new DockPanel { Margin = new Thickness(16) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            layout.Children.Add(buttons);
            layout.Children.Add(text);
            window.Content = layout;
            text.Focus();
            text.SelectAll();
            window.ShowDialog();
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
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = PanelTheme.InkBrush()
            };
            repository.SetBinding(TextBlock.TextProperty, new Binding("RepositoryPath"));

            TextBlock branch = new()
            {
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = PanelTheme.InkBrush()
            };
            branch.SetBinding(TextBlock.TextProperty, new Binding("Status.CurrentBranch")
            {
                StringFormat = "Branch: {0}"
            });

            TextBlock tracking = new()
            {
                Foreground = PanelTheme.MutedBrush()
            };
            tracking.SetBinding(TextBlock.TextProperty, new Binding("Status.TrackingSummary"));

            TextBlock dirty = new()
            {
                Foreground = PanelTheme.MutedBrush()
            };
            dirty.SetBinding(TextBlock.TextProperty, new Binding("Status.StatusSummary"));

            header.Children.Add(repository);
            header.Children.Add(branch);
            StackPanel summary = new() { Orientation = Orientation.Horizontal };
            summary.Children.Add(tracking);
            summary.Children.Add(dirty);
            dirty.Margin = new Thickness(12, 0, 0, 0);
            header.Children.Add(summary);
            return header;
        }

        private static StackPanel ActionRow(params FrameworkElement[] items)
        {
            StackPanel row = new()
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            foreach (FrameworkElement item in items)
            {
                row.Children.Add(item);
            }

            return row;
        }

        private static CheckBox ExportChoice(string content, string path, string toolTip)
        {
            CheckBox box = new()
            {
                Content = content,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = toolTip
            };
            box.SetBinding(CheckBox.IsCheckedProperty, new Binding(path)
            {
                Mode = BindingMode.TwoWay
            });
            return box;
        }

        private static Button HmiButton(string content, string commandPath, string toolTip)
        {
            ResourceDictionary theme = PanelTheme.Create();
            Button button = new()
            {
                Content = content,
                Padding = new Thickness(10, 2, 10, 2),
                Margin = new Thickness(0, 0, 12, 0),
                ToolTip = toolTip,
                Style = content == "Transfer to project"
                    ? (Style)theme["DangerButtonStyle"]
                    : (Style)theme["PrimaryButtonStyle"]
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
                        Header = "Changes",
                        Content = BuildChangesPage(vm)
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
                        Header = "Export",
                        Content = new ExportView { DataContext = vm.Export }
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
                    },
                    new TabItem
                    {
                        Header = "About",
                        Content = new AboutView { DataContext = vm.About }
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

        private static FrameworkElement BuildChangesPage(MainViewModel vm)
        {
            DockPanel page = new();
            FrameworkElement actions = BuildChangeActions();
            DockPanel.SetDock(actions, Dock.Bottom);
            page.Children.Add(actions);
            page.Children.Add(new StatusView { DataContext = vm.Status });
            return page;
        }

        private static FrameworkElement BuildChangeActions()
        {
            StackPanel panel = new()
            {
                Margin = new Thickness(10, 0, 10, 8)
            };
            panel.Children.Add(ActionRow(
                ExportChoice("HMI", "Hmi.ExportHmi", "Include the open Unified or WinCC Comfort device. Writes HmiExport/<device>/."),
                ExportChoice("Hardware", "Hmi.ExportHardware", "Include devices, modules, networks, and drive parameters that differ from the factory default. Writes HardwareExport/."),
                HmiButton("Transfer to Git", "Hmi.ExportCommand", "Write the checked items into the Git folder. HMI and hardware can run together."),
                ExportChoice("HMI", "Hmi.ImportHmi", "Apply HmiExport/<device>/ back onto the open device after a checkout."),
                ExportChoice("Hardware", "Hmi.ImportHardware", "Apply addresses, port links, and changed drive parameters onto the matching devices. Modules are not created or deleted."),
                HmiButton("Transfer to project", "Hmi.ImportCommand", "Write the checked items onto the open project.")));

            TextBlock transferStatus = new()
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            Style statusStyle = new(typeof(TextBlock));
            statusStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, PanelTheme.InkBrush()));
            DataTrigger failed = new()
            {
                Binding = new Binding("Hmi.LastOperationFailed"),
                Value = true
            };
            failed.Setters.Add(new Setter(TextBlock.ForegroundProperty, PanelTheme.DangerBrush()));
            statusStyle.Triggers.Add(failed);
            transferStatus.Style = statusStyle;
            transferStatus.SetBinding(TextBlock.TextProperty, new Binding("Hmi.LastOperationMessage"));
            panel.Children.Add(transferStatus);

            TextBox message = new()
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 64,
                MaxHeight = 120,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 8, 0, 0)
            };
            message.SetBinding(TextBox.TextProperty, new Binding("Commit.CommitMessage")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            panel.Children.Add(message);

            DockPanel commitRow = new() { Margin = new Thickness(0, 6, 0, 0) };
            TextBlock staged = new() { VerticalAlignment = VerticalAlignment.Center };
            staged.SetBinding(TextBlock.TextProperty, new Binding("Status.StagedCount")
            {
                StringFormat = "{0} staged"
            });
            TextBlock validation = new()
            {
                Foreground = PanelTheme.DangerBrush(),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            validation.SetBinding(TextBlock.TextProperty, new Binding("Commit.ValidationMessage"));
            TextBlock commitStatus = new()
            {
                Foreground = PanelTheme.MutedBrush(),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            commitStatus.SetBinding(TextBlock.TextProperty, new Binding("Commit.LastOperationMessage"));
            Button commit = new()
            {
                Content = "Commit",
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(18, 4, 18, 4),
                Style = (Style)PanelTheme.Create()["PrimaryButtonStyle"]
            };
            commit.SetBinding(Button.CommandProperty, new Binding("Commit.CommitCommand"));
            DockPanel.SetDock(commit, Dock.Right);
            commitRow.Children.Add(commit);
            StackPanel notes = new() { Orientation = Orientation.Horizontal };
            notes.Children.Add(staged);
            notes.Children.Add(validation);
            notes.Children.Add(commitStatus);
            commitRow.Children.Add(notes);
            panel.Children.Add(commitRow);
            return panel;
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

        private void OnHmiPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(HmiExchangeViewModel.HasChecklist))
            {
                return;
            }

            if (viewModel.Hmi.HasChecklist)
            {
                ShowTransferWindow();
            }
            else
            {
                CloseTransferWindow();
            }
        }

        private void ShowTransferWindow()
        {
            if (transferWindow != null)
            {
                if (!transferWindow.IsVisible)
                {
                    transferWindow.Show();
                }

                return;
            }

            transferWindow = new TransferStatusWindow(viewModel, () => panelClosing)
            {
                Owner = this
            };
            transferWindow.Closed += (_, _) => transferWindow = null;
            transferWindow.Show();
        }

        private void CloseTransferWindow()
        {
            TransferStatusWindow? window = transferWindow;
            if (window == null)
            {
                return;
            }

            transferWindow = null;
            window.RequestClose();
        }

        private static FrameworkElement BuildBusyOverlay()
        {
            Grid overlay = new()

            {
                Background = new SolidColorBrush(Color.FromArgb(160, 20, 20, 20)),
                IsHitTestVisible = true
            };
            overlay.SetBinding(UIElement.VisibilityProperty, new Binding("ShowOverlay")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            Border card = new()

            {
                Background = new SolidColorBrush(Color.FromArgb(245, 30, 30, 30)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(32, 24, 32, 24),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 900
            };
            card.SetBinding(FrameworkElement.MinWidthProperty, new Binding("OverlayCardMinWidth"));

            StackPanel panel = new()

            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            ProgressBar progress = new()

            {
                IsIndeterminate = true,
                Height = 4,
                Margin = new Thickness(0, 0, 0, 16),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            progress.SetBinding(UIElement.VisibilityProperty, new Binding("ShowProgress")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            TextBlock message = new()

            {
                Foreground = Brushes.White,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 16),
                TextAlignment = TextAlignment.Left,
                TextWrapping = TextWrapping.Wrap
            };
            message.SetBinding(TextBlock.TextProperty, new Binding("OverlayStatus"));
            message.SetBinding(UIElement.VisibilityProperty, new Binding("HasOverlayStatus")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            Button cancel = new()

            {
                MinWidth = 90,
                Padding = new Thickness(20, 6, 20, 6),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            cancel.SetBinding(ContentControl.ContentProperty, new Binding("OverlayActionText"));
            cancel.SetBinding(Button.CommandProperty, new Binding("OverlayActionCommand"));

            TextBlock timer = new()
            {
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12)
            };
            timer.SetBinding(TextBlock.TextProperty, new Binding("Hmi.ElapsedText"));
            timer.SetBinding(UIElement.VisibilityProperty, new Binding("Hmi.HasElapsed")
            {
                Converter = new BooleanToVisibilityConverter()
            });

            panel.Children.Add(timer);
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
