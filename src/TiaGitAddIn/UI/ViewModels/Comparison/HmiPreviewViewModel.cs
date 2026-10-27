using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using TiaGitAddIn.Models.Hmi;

namespace TiaGitAddIn.UI.ViewModels.Comparison
{
    public sealed class HmiScreenBoxViewModel
    {
        public HmiScreenBoxViewModel(HmiScreenItemPreview item, double scale)
        {
            Kind = item.Kind;
            Change = item.Change;
            Label = string.IsNullOrWhiteSpace(item.Label) ? item.Name : item.Label;
            ScaledLeft = Math.Max(0, item.Left) * scale;
            ScaledTop = Math.Max(0, item.Top) * scale;
            ScaledWidth = Math.Max(8, item.Width) * scale;
            ScaledHeight = Math.Max(8, item.Height) * scale;
            bool marked = Change == "Added" || Change == "Removed" || Change == "Changed";
            Fill = marked ? FillFor(Change) : Freeze(Color.FromRgb(244, 244, 244));
            Edge = marked ? EdgeFor(Change) : Freeze(Color.FromRgb(208, 208, 208));
            EdgeThickness = marked ? 3 : 1;
            Ink = marked ? Freeze(Color.FromRgb(20, 20, 20)) : Freeze(Color.FromRgb(150, 150, 150));
            LabelWeight = marked ? FontWeights.Bold : FontWeights.Normal;
            ToolTipText = marked
                ? Change + ": " + Label + (string.IsNullOrEmpty(item.Detail) ? string.Empty : " — " + item.Detail)
                : Label;
        }

        public string Kind { get; }
        public string Change { get; }
        public string Label { get; }
        public double ScaledLeft { get; }
        public double ScaledTop { get; }
        public double ScaledWidth { get; }
        public double ScaledHeight { get; }
        public Brush Fill { get; }
        public Brush Edge { get; }
        public double EdgeThickness { get; }
        public Brush Ink { get; }
        public FontWeight LabelWeight { get; }
        public string ToolTipText { get; }

        private static Brush FillFor(string change)
        {
            if (change == "Added")
            {
                return Freeze(Color.FromRgb(125, 206, 160));
            }

            if (change == "Removed")
            {
                return Freeze(Color.FromRgb(241, 148, 138));
            }

            return Freeze(Color.FromRgb(247, 220, 111));
        }

        private static Brush EdgeFor(string change)
        {
            if (change == "Added")
            {
                return Freeze(Color.FromRgb(30, 132, 73));
            }

            if (change == "Removed")
            {
                return Freeze(Color.FromRgb(192, 57, 43));
            }

            return Freeze(Color.FromRgb(183, 149, 11));
        }

        private static SolidColorBrush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    public sealed class HmiScreenPaneViewModel
    {
        public HmiScreenPaneViewModel(string title, HmiScreenSidePreview screen, double scale)
        {
            Title = title;
            IsMissing = screen == null;
            int width = screen == null || screen.Width <= 0 ? 800 : screen.Width;
            int height = screen == null || screen.Height <= 0 ? 480 : screen.Height;
            CanvasWidth = Math.Max(40, width * scale);
            CanvasHeight = Math.Max(30, height * scale);
            var boxes = new List<HmiScreenBoxViewModel>();
            if (screen != null)
            {
                foreach (HmiScreenItemPreview item in screen.Items)
                {
                    boxes.Add(new HmiScreenBoxViewModel(item, scale));
                }

                boxes.Sort((left, right) => Rank(left.Change).CompareTo(Rank(right.Change)));
            }

            Boxes = boxes;
            int marked = 0;
            foreach (HmiScreenBoxViewModel box in boxes)
            {
                if (box.Change != "Unchanged")
                {
                    marked++;
                }
            }

            Title = marked == 0 ? title : title + " · " + marked.ToString() + " highlighted";
        }

        private static int Rank(string change)
        {
            if (change == "Added" || change == "Removed" || change == "Changed")
            {
                return 1;
            }

            return 0;
        }

        public string Title { get; }
        public bool IsMissing { get; }
        public double CanvasWidth { get; }
        public double CanvasHeight { get; }
        public IReadOnlyList<HmiScreenBoxViewModel> Boxes { get; }
    }

    public static class HmiPreviewBuilder
    {
        public static IReadOnlyList<HmiScreenPaneViewModel> Screens(HmiFilePreview? preview)
        {
            if (preview == null || !preview.IsScreen)
            {
                return Array.Empty<HmiScreenPaneViewModel>();
            }

            int width = Math.Max(
                preview.LeftScreen == null ? 0 : preview.LeftScreen.Width,
                preview.RightScreen == null ? 0 : preview.RightScreen.Width);
            int height = Math.Max(
                preview.LeftScreen == null ? 0 : preview.LeftScreen.Height,
                preview.RightScreen == null ? 0 : preview.RightScreen.Height);
            if (width <= 0)
            {
                width = 800;
            }

            if (height <= 0)
            {
                height = 480;
            }

            double scale = Math.Min(420d / width, 240d / height);
            if (scale <= 0 || double.IsInfinity(scale))
            {
                scale = 1;
            }

            return new[]
            {
                new HmiScreenPaneViewModel("Previous", preview.LeftScreen, scale),
                new HmiScreenPaneViewModel("Current", preview.RightScreen, scale)
            };
        }
    }
}
