using System;
using AutoCommand.Agent.Core;
using AutoCommand.Contain.Core;
using AutoCommand.Forensics.Core;
using AutoCommand.NetWatch.Core;
using AutoCommand.Sitedoctor.Core;
using System.Globalization;
using System.Windows.Data;
using AutoCommand.Models;
using AutoCommand.Services;

namespace AutoCommand.Converters
{
    /// <summary>
    /// Resolves an AppPackageItem grid row to its bloatware-toggle button label,
    /// or to the button tooltip when ConverterParameter is "tooltip". System
    /// components and the synthetic OneDrive row are not toggleable and
    /// render empty (the button collapses via a style trigger).
    /// </summary>
    public class BloatwareStateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not AppPackageItem app) return "";
            if (app.IsSystem || app.FullName == AppManagerService.OneDriveMarkerFullName) return "";

            bool tooltip = string.Equals(parameter as string, "tooltip", StringComparison.OrdinalIgnoreCase);
            switch (AppManagerService.GetMatchSource(app.PackageName, app.Name, out _))
            {
                case AppManagerService.BloatwareMatchSource.DefaultPattern:
                    return tooltip
                        ? "Matched by a default bloatware pattern — click to stop treating it as bloatware."
                        : "✓ Bloatware";
                case AppManagerService.BloatwareMatchSource.CustomPattern:
                    return tooltip
                        ? "Matched by a custom pattern — click to remove that pattern."
                        : "✓ Custom pattern";
                case AppManagerService.BloatwareMatchSource.CustomPackage:
                    return tooltip
                        ? "Added as a custom bloatware app — click to remove it."
                        : "✓ Custom app";
                default:
                    return tooltip
                        ? "Click to treat this app as bloatware (adds its exact package name)."
                        : "＋ Bloatware";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
