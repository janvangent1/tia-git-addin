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
            FontSize = 14;
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

            header.Children.Add(ActionRow(
                ExportChoice(
                    "HMI",
                    "Hmi.ExportHmi",
                    "Include the open Unified or WinCC Comfort device. Writes HmiExport/<device>/."),
                ExportChoice(
                    "Hardware",
                    "Hmi.ExportHardware",
                    "Include devices, modules, networks, and drive parameters that differ from the factory default. Writes HardwareExport/."),
                HmiButton(
                    "Export",
                    "Hmi.ExportCommand",
                    "Export the checked items. HMI and hardware can run together.")));
            StackPanel importRow = ActionRow(
                ExportChoice(
                    "HMI",
                    "Hmi.ImportHmi",
                    "Apply HmiExport/<device>/ back onto the open Unified or WinCC Comfort device (panel or runtime) after a checkout."),
                ExportChoice(
                    "Hardware",
                    "Hmi.ImportHardware",
                    "Apply node addresses, port links, and changed drive parameters from HardwareExport onto the matching open devices. Modules are not created or deleted."),
                HmiButton(
                    "Import",
                    "Hmi.ImportCommand",
                    "Import the checked items. HMI and hardware can run together."));
            TextBlock hmiMessage = new()
            {
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.DimGray,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            hmiMessage.SetBinding(TextBlock.TextProperty, new Binding("Hmi.LastOperationMessage"));
            importRow.Children.Add(hmiMessage);
            header.Children.Add(importRow);
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

            ItemsControl checklist = new()
            {
                ItemTemplate = ChecklistTemplate(),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            checklist.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Hmi.ChecklistGroups"));
            ScrollViewer list = new()
            {
                Content = checklist,
                MaxHeight = 640,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 0, 0, 16)
            };
            list.SetBinding(UIElement.VisibilityProperty, new Binding("Hmi.HasChecklist")
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

            panel.Children.Add(progress);
            panel.Children.Add(message);
            panel.Children.Add(list);
            panel.Children.Add(cancel);
            card.Child = panel;
            overlay.Children.Add(card);
            return overlay;
        }

        private static DataTemplate ChecklistTemplate()
        {
            var expander = new FrameworkElementFactory(typeof(Expander));
            expander.SetBinding(Expander.IsExpandedProperty, new Binding(nameof(HmiChecklistGroup.IsExpanded))
            {
                Mode = BindingMode.TwoWay
            });
            expander.SetBinding(HeaderedContentControl.HeaderProperty, new Binding(nameof(HmiChecklistGroup.Header)));
            expander.SetValue(Control.ForegroundProperty, Brushes.White);
            expander.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            expander.SetValue(Control.FontSizeProperty, 13.0);
            expander.SetValue(Control.PaddingProperty, new Thickness(0, 3, 0, 3));
            expander.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1));

            var children = new FrameworkElementFactory(typeof(ItemsControl));
            children.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(HmiChecklistGroup.Items)));
            children.SetValue(ItemsControl.ItemTemplateProperty, CategoryTemplate());
            children.SetValue(FrameworkElement.MarginProperty, new Thickness(22, 2, 0, 6));
            expander.AppendChild(children);
            return new DataTemplate { VisualTree = expander };
        }

        private static DataTemplate CategoryTemplate()
        {
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            row.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1));

            var mark = new FrameworkElementFactory(typeof(TextBlock));
            mark.SetBinding(TextBlock.TextProperty, new Binding(nameof(HmiChecklistItem.Mark)));
            mark.SetValue(TextBlock.WidthProperty, 24.0);
            mark.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            mark.SetValue(TextBlock.FontSizeProperty, 13.0);

            var line = new FrameworkElementFactory(typeof(TextBlock));
            line.SetBinding(TextBlock.TextProperty, new Binding(nameof(HmiChecklistItem.Line)));
            line.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            line.SetValue(TextBlock.FontSizeProperty, 13.0);
            line.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            line.SetValue(TextBlock.MaxWidthProperty, 720.0);

            row.AppendChild(mark);
            row.AppendChild(line);
            return new DataTemplate { VisualTree = row };
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
