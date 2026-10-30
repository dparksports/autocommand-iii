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
    /// Converts task state to color.
    /// </summary>
    public class TaskStateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string state)
            {
                return state switch
                {
                    "Running" => new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F)),   // Green
                    "Ready" => new SolidColorBrush(Color.FromRgb(0x4C, 0x9E, 0xEB)),      // Blue
                    "Disabled" => new SolidColorBrush(Color.FromRgb(0x9D, 0x9D, 0xA5)),   // Gray
                    _ => new SolidColorBrush(Color.FromRgb(0xE5, 0xA6, 0x31))             // Amber
                };
            }
            return new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts service status string to colored indicator.
    /// </summary>
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value?.ToString()?.ToLower() ?? "";
            if (status.Contains("stopped") || status.Contains("disabled") || status.Contains("off") || status.Contains("not found"))
                return new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x6F)); // Green = secure (stopped is good)
            if (status.Contains("running") || status.Contains("enabled") || status.Contains("on"))
                return new SolidColorBrush(Color.FromRgb(0xE5, 0xA6, 0x31)); // Amber = attention
            return new SolidColorBrush(Color.FromRgb(0x9D, 0x9D, 0xA5)); // Gray
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts empty app names to "All Applications".
    /// </summary>
    public class AppNameToDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string app = value?.ToString();
            if (string.IsNullOrWhiteSpace(app) || app == "Any") return "All Applications";
            return app;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
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

    /// <summary>
    /// Group expander state for the Traffic Patterns grid: every group starts
    /// expanded and the user's collapse choice is remembered per group name,
    /// so the OneWay binding re-evaluating on container recreation or group
    /// ItemCount changes never overrides what the user chose. Pair with
    /// Expanded/Collapsed handlers that update CollapsedGroups.
    /// </summary>
    public class GroupExpandedStateConverter : IValueConverter
    {
        /// <summary>Group names the user collapsed (groups start expanded).</summary>
        public static readonly HashSet<string> CollapsedGroups = new(StringComparer.Ordinal);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is System.Windows.Data.CollectionViewGroup group &&
                     group.Name is string name &&
                     CollapsedGroups.Contains(name));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}

