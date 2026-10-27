using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TiaGitAddIn.UI.ViewModels;

namespace TiaGitAddIn.UI.Views
{
    /// <summary>
    /// Transfer progress in its own window. Minimizing it leaves the Git panel free to use.
    /// Closing it while a transfer is running minimizes it instead of stopping the transfer.
    /// </summary>
    public sealed class TransferStatusWindow : Window
    {
        private readonly MainViewModel viewModel;
        private readonly System.Func<bool> ownerIsClosing;
        private bool allowClose;
        private bool closing;

        public TransferStatusWindow(MainViewModel viewModel, System.Func<bool> ownerIsClosing)
        {
            this.viewModel = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));
            this.ownerIsClosing = ownerIsClosing ?? throw new System.ArgumentNullException(nameof(ownerIsClosing));
            PanelTheme.Apply(this);
            DataContext = viewModel;
            Title = "Transfer";
            Width = 560;
            Height = 460;
            MinWidth = 420;
            MinHeight = 280;
            ShowInTaskbar = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = PanelTheme.SurfaceBrush();
            Foreground = PanelTheme.InkBrush();
            FontSize = 14;
            Content = BuildBody();
        }

        public void RequestClose()
        {
            allowClose = true;
            if (closing || !IsVisible)
            {
                return;
            }

            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (closing)
            {
                base.OnClosing(e);
                return;
            }

            if (!allowClose && !ownerIsClosing())
            {
                if (viewModel.Hmi.IsBusy)
                {
                    e.Cancel = true;
                    WindowState = WindowState.Minimized;
                    return;
                }

                allowClose = true;
                closing = true;
                if (viewModel.Hmi.HasChecklist)
                {
                    viewModel.Hmi.DismissChecklistCommand.Execute(null);
                }
            }

            closing = true;
            base.OnClosing(e);
        }

        private FrameworkElement BuildBody()
        {
            DockPanel dock = new()
            {
                Margin = new Thickness(12)
            };

            StackPanel actions = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Button minimize = new()
            {
                Content = "Minimize",
                MinWidth = 90,
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Hide this window and keep using the Git panel. The transfer keeps running."
            };
            minimize.Click += (_, _) => WindowState = WindowState.Minimized;
            Button close = new()
            {
                MinWidth = 90,
                Padding = new Thickness(16, 4, 16, 4)
            };
            close.SetBinding(ContentControl.ContentProperty, new Binding("OverlayActionText"));
            close.SetBinding(Button.CommandProperty, new Binding("OverlayActionCommand"));
            actions.Children.Add(minimize);
            actions.Children.Add(close);
            DockPanel.SetDock(actions, Dock.Bottom);

            TextBlock timer = new()
            {
                Foreground = PanelTheme.InkBrush(),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            timer.SetBinding(TextBlock.TextProperty, new Binding("Hmi.ElapsedText"));
            timer.SetBinding(UIElement.VisibilityProperty, new Binding("Hmi.HasElapsed")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            DockPanel.SetDock(timer, Dock.Top);

            TextBlock message = new()
            {
                Foreground = PanelTheme.InkBrush(),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            message.SetBinding(TextBlock.TextProperty, new Binding("OverlayStatus"));
            DockPanel.SetDock(message, Dock.Top);

            ItemsControl checklist = new()
            {
                ItemTemplate = ChecklistTemplate()
            };
            checklist.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Hmi.ChecklistGroups"));
            ScrollViewer list = new()
            {
                Content = checklist,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            dock.Children.Add(actions);
            dock.Children.Add(timer);
            dock.Children.Add(message);
            dock.Children.Add(list);
            return dock;
        }

        private static DataTemplate ChecklistTemplate()
        {
            var expander = new FrameworkElementFactory(typeof(Expander));
            expander.SetBinding(Expander.IsExpandedProperty, new Binding(nameof(HmiChecklistGroup.IsExpanded))
            {
                Mode = BindingMode.TwoWay
            });
            expander.SetBinding(HeaderedContentControl.HeaderProperty, new Binding(nameof(HmiChecklistGroup.Header)));
            expander.SetValue(Control.ForegroundProperty, PanelTheme.InkBrush());
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
            mark.SetValue(TextBlock.ForegroundProperty, PanelTheme.InkBrush());
            mark.SetValue(TextBlock.FontSizeProperty, 13.0);

            var line = new FrameworkElementFactory(typeof(TextBlock));
            line.SetBinding(TextBlock.TextProperty, new Binding(nameof(HmiChecklistItem.Line)));
            line.SetValue(TextBlock.ForegroundProperty, PanelTheme.InkBrush());
            line.SetValue(TextBlock.FontSizeProperty, 13.0);
            line.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);

            row.AppendChild(mark);
            row.AppendChild(line);
            return new DataTemplate { VisualTree = row };
        }
    }
}
