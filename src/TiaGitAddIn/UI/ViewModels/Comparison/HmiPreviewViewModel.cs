using System;
using System.Collections.Generic;
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
        }

        public string Kind { get; }
        public string Change { get; }
        public string Label { get; }
        public double ScaledLeft { get; }
        public double ScaledTop { get; }
        public double ScaledWidth { get; }
        public double ScaledHeight { get; }
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
            }

            Boxes = boxes;
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
