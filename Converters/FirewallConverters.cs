using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

using AutoCommand.Agent.Core;
using AutoCommand.Contain.Core;
using AutoCommand.Forensics.Core;
using AutoCommand.NetWatch.Core;
using AutoCommand.Sitedoctor.Core;

namespace AutoCommand.Converters
{
    /// <summary>
    /// Converts boolean enabled state to colored text for DataGrid display.
    /// </summary>
    public class EnabledToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool enabled)
            {
                return enabled
                    ? new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F))  // AccentGreen
                    : new SolidColorBrush(Color.FromRgb(0x9D, 0x9D, 0xA5)); // TextSecondary
            }
            return new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts firewall action (Allow/Block) to color.
    /// </summary>
    public class ActionToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string action)
            {
                return action == "Allow"
                    ? new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F))  // Green
                    : new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B)); // Red
            }
            return new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Evaluates if a CollectionViewGroup has 5 or fewer items.
    /// </summary>
    public class GroupSizeExpanderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is System.Windows.Data.CollectionViewGroup group)
            {
                return group.ItemCount <= 5;
            }
            if (value is int count)
            {
                return count <= 5;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

}

