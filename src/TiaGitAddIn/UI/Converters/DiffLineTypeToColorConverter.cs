using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TiaGitAddIn.Models;
using TiaGitAddIn.UI;

namespace TiaGitAddIn.UI.Converters
{
    public sealed class DiffLineTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is DiffLineType type)) return Brushes.Transparent;

            switch (type)
            {
                case DiffLineType.Added: return new SolidColorBrush(PanelTheme.Added);
                case DiffLineType.Deleted: return new SolidColorBrush(PanelTheme.Removed);
                case DiffLineType.Header: return new SolidColorBrush(PanelTheme.Changed);
                case DiffLineType.Context:
                default: return Brushes.Transparent;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
