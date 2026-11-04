using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace TiaGitAddIn.UI
{
    /// <summary>
    /// Light and dark colors for the Git panel. WPF does not follow the Windows app theme on its own.
    /// </summary>
    public static class PanelTheme
    {
        public static readonly bool Dark = DetectDark();

        public static Color Added => Dark ? Color.FromRgb(28, 92, 52) : Color.FromRgb(221, 255, 221);
        public static Color Removed => Dark ? Color.FromRgb(122, 48, 48) : Color.FromRgb(255, 221, 221);
        public static Color Changed => Dark ? Color.FromRgb(120, 104, 36) : Color.FromRgb(255, 244, 180);
        public static Color Ink => Dark ? Color.FromRgb(232, 232, 232) : Color.FromRgb(24, 24, 24);
        public static Color Muted => Dark ? Color.FromRgb(176, 176, 176) : Color.FromRgb(105, 105, 105);
        public static Color Surface => Dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(255, 255, 255);
        public static Color Card => Dark ? Color.FromRgb(48, 48, 48) : Color.FromRgb(245, 245, 245);
        public static Color Line => Dark ? Color.FromRgb(78, 78, 78) : Color.FromRgb(221, 221, 221);
        public static Color Danger => Color.FromRgb(176, 48, 48);
        public static Color Primary => Dark ? Color.FromRgb(88, 146, 214) : Color.FromRgb(24, 92, 168);
        public static Color Gap => Dark ? Color.FromRgb(92, 74, 28) : Color.FromRgb(255, 244, 214);

        public static void Apply(FrameworkElement element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            element.Resources.MergedDictionaries.Add(Create());
        }

        public static ResourceDictionary Create()
        {
            var dictionary = new ResourceDictionary();
            dictionary["AddedBrush"] = Brush(Added);
            dictionary["RemovedBrush"] = Brush(Removed);
            dictionary["ChangedBrush"] = Brush(Changed);
            dictionary["PanelInkBrush"] = Brush(Ink);
            dictionary["MutedBrush"] = Brush(Muted);
            dictionary["SurfaceBrush"] = Brush(Surface);
            dictionary["CardBrush"] = Brush(Card);
            dictionary["LineBrush"] = Brush(Line);
            dictionary["DangerBrush"] = Brush(Danger);
            dictionary["PrimaryBrush"] = Brush(Primary);
            dictionary["GapBrush"] = Brush(Gap);
            dictionary["PrimaryButtonStyle"] = ButtonStyle(Primary, Colors.White);
            dictionary["DangerButtonStyle"] = ButtonStyle(Danger, Colors.White);

            var list = new Style(typeof(ListView));
            list.Setters.Add(new Setter(Control.BackgroundProperty, Brush(Surface)));
            list.Setters.Add(new Setter(Control.ForegroundProperty, Brush(Ink)));
            dictionary.Add(typeof(ListView), list);

            var tree = new Style(typeof(TreeView));
            tree.Setters.Add(new Setter(Control.BackgroundProperty, Brush(Surface)));
            tree.Setters.Add(new Setter(Control.ForegroundProperty, Brush(Ink)));
            dictionary.Add(typeof(TreeView), tree);
            return dictionary;
        }

        public static Brush InkBrush() => Brush(Ink);

        public static Brush SurfaceBrush() => Brush(Surface);

        public static Brush MutedBrush() => Brush(Muted);

        public static Brush CardBrush() => Brush(Card);

        public static Brush LineBrush() => Brush(Line);

        public static Brush DangerBrush() => Brush(Danger);

        private static SolidColorBrush Brush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Style ButtonStyle(Color background, Color foreground)
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brush(background)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Brush(foreground)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 4, 12, 4)));
            return style;
        }

        private static bool DetectDark()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                object? value = key?.GetValue("AppsUseLightTheme");
                return value is int light && light == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
