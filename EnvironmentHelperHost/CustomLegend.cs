using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView.SKCharts;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace EnvironmentHelperHost;

public class CustomLegend : SKDefaultLegend
{
    private const int SzIndex = 10050;

    private readonly SolidColorPaint _fontPaint = new(new SKColor(30, 20, 30))
    {
        SKTypeface = SKTypeface.FromFamilyName("Microsoft YaHei UI", SKFontStyle.Bold),
        ZIndex = SzIndex + 1
    };

    public CustomLegend()
    {
        FontPaint = _fontPaint;
    }
}