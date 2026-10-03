using System.Windows;
using System.Windows.Controls;
namespace ControlCenter.App.Views;
// Preserve configured module order; adjacent compact cards share a row when room permits.
public sealed class ControlCardPanel : Panel
{
    private const double Gap = 10;
    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? 388 : available.Width;
        return new(width, Layout(width, false));
    }
    protected override Size ArrangeOverride(Size final) { Layout(final.Width, true); return final; }
    private double Layout(double width, bool arrange)
    {
        var children = InternalChildren.Cast<FrameworkElement>().Where(x => x.Visibility != Visibility.Collapsed).ToArray();
        double y = 0;
        for (int i = 0; i < children.Length; i++)
        {
            var first = children[i];
            bool pair = width >= 350 && (string?)first.Tag == "Compact" && i + 1 < children.Length && (string?)children[i + 1].Tag == "Compact";
            double cell = pair ? (width - Gap) / 2 : width;
            if (!arrange) first.Measure(new(cell, double.PositiveInfinity));
            double height = first.DesiredSize.Height;
            FrameworkElement? second = pair ? children[++i] : null;
            if (second is not null) { if (!arrange) second.Measure(new(cell, double.PositiveInfinity)); height = Math.Max(height, second.DesiredSize.Height); }
            if (arrange)
            {
                first.Arrange(new Rect(0, y, cell, height));
                second?.Arrange(new Rect(cell + Gap, y, cell, height));
            }
            y += height + Gap;
        }
        return Math.Max(0, y - Gap);
    }
}
