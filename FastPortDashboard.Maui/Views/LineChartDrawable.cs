using System.Globalization;
using FastPortDashboard.Core.Charts;
using Microsoft.Maui.Graphics;

namespace FastPortDashboard.Maui.Views;

// Design Ref: §2 (dashboard-chart-graphicsview-migration) — SkiaSharp/Microcharts를
// Microsoft.Maui.Graphics IDrawable로 교체. macOS 26 SwiftUI Observation crash 회피.
// Snapshot 패턴: Values는 매 업데이트마다 외부에서 새 list로 교체 후 GraphicsView.Invalidate().
public sealed class LineChartDrawable : IDrawable
{
    public IReadOnlyList<double> Values { get; set; } = Array.Empty<double>();
    public Color LineColor { get; set; } = Colors.DodgerBlue;
    public float LineWidth { get; set; } = 2f;
    public string ValueFormat { get; set; } = "F0";
    public bool ShowLastValueLabel { get; set; } = true;

    // Design Ref: §11.2 — 라인 + 마지막 값 라벨.
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var values = Values;
        if (values is null || values.Count == 0 || dirtyRect.Width <= 0f || dirtyRect.Height <= 0f)
        {
            return;
        }

        var (min, max) = LineChartMath.ComputeRange(values);
        double range = max - min;
        if (range <= 0d) range = 1d;

        const float marginTop = 10f;
        const float marginBottom = 6f;
        const float labelReserveRight = 60f;

        float plotHeight = dirtyRect.Height - marginTop - marginBottom;
        if (plotHeight <= 0f) plotHeight = dirtyRect.Height;

        int n = values.Count;
        float stepX = LineChartMath.ComputeStepX(n, dirtyRect.Width);

        canvas.StrokeColor = LineColor;
        canvas.StrokeSize = LineWidth;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        if (n == 1)
        {
            // 단일 sample — 점 하나만.
            float x = dirtyRect.X + dirtyRect.Width - labelReserveRight - 4f;
            float yNorm = 0.5f;
            float y = dirtyRect.Y + marginTop + (1f - yNorm) * plotHeight;
            canvas.FillColor = LineColor;
            canvas.FillCircle(x, y, LineWidth);
        }
        else
        {
            var path = new PathF();
            for (int i = 0; i < n; i++)
            {
                float x = dirtyRect.X + i * stepX;
                float yNorm = (float)((values[i] - min) / range);
                float y = dirtyRect.Y + marginTop + (1f - yNorm) * plotHeight;
                if (i == 0) path.MoveTo(x, y);
                else path.LineTo(x, y);
            }
            canvas.DrawPath(path);
        }

        if (ShowLastValueLabel && n > 0)
        {
            double lastVal = values[n - 1];
            canvas.FontColor = LineColor;
            canvas.FontSize = 11f;
            canvas.DrawString(
                lastVal.ToString(ValueFormat, CultureInfo.InvariantCulture),
                dirtyRect.X + dirtyRect.Width - labelReserveRight,
                dirtyRect.Y + 2f,
                labelReserveRight - 2f,
                14f,
                HorizontalAlignment.Right,
                VerticalAlignment.Top);
        }
    }
}
