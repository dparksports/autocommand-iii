using System.Windows;

using AutoCommand.Agent.Core;
using AutoCommand.Contain.Core;
using AutoCommand.Forensics.Core;
using AutoCommand.NetWatch.Core;
using AutoCommand.Sitedoctor.Core;

namespace AutoCommand.Helpers
{
    /// <summary>
    /// WPF's Button has no CornerRadius property (that's WinUI/UWP) — this
    /// attached property lets button styles drive the templated Border's
    /// radius via {TemplateBinding helpers:Ui.CornerRadius}, so base buttons
    /// get 10px rounding and Primary/Danger styles get full pills (22px).
    /// </summary>
    public static class Ui
    {
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.RegisterAttached(
                "CornerRadius", typeof(CornerRadius), typeof(Ui),
                new PropertyMetadata(new CornerRadius(10)));

        public static CornerRadius GetCornerRadius(DependencyObject obj) =>
            (CornerRadius)obj.GetValue(CornerRadiusProperty);

        public static void SetCornerRadius(DependencyObject obj, CornerRadius value) =>
            obj.SetValue(CornerRadiusProperty, value);
    }
}
