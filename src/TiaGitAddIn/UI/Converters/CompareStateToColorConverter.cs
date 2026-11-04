using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TiaGitAddIn.Models.Sact;
using TiaGitAddIn.UI;

namespace TiaGitAddIn.UI.Converters
{
    public class CompareStateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CompareState state)
            {
                switch (state)
                {
                    case CompareState.Changed:
                        return new SolidColorBrush(PanelTheme.Changed);
                    case CompareState.MissingOnLeft:
                        return new SolidColorBrush(PanelTheme.Added);
                    case CompareState.MissingOnRight:
                        return new SolidColorBrush(PanelTheme.Removed);
                    case CompareState.Equal:
                    default:
                        return Brushes.Transparent;
                }
            }
            
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}