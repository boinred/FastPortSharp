using FastPortDashboard.Core.Charts;
using Microsoft.Maui.Graphics;

namespace FastPortDashboard.Maui.Views;

// Design Ref: §3.2 (dashboard-chart-multi-rtt-overlay-v2) — N-series 통합 좌표계 라인 차트.
// SkiaSharp 미사용. macOS 26 SwiftUI Observation crash 회피책 준수.
public sealed class MultiLineChartDrawable : IDrawable
{
    public IReadOnlyList<LineChartSeries> Series { get; set; } = Array.Empty<LineChartSeries>();
    public bool ShowLegend { get; set; } = true;

    // Design Ref: §11.2 — Draw 알고리즘.
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var allSeries = Series;
        if (allSeries is null || allSeries.Count == 0 ||
            dirtyRect.Width <= 0f || dirtyRect.Height <= 0f)
        {
            return;
        }

        // 비어있는 series는 그리지 않음.
        var nonEmpty = new List<LineChartSeries>(allSeries.Count);
        foreach (var s in allSeries)
        {
            if (s is not null && s.Values is not null && s.Values.Count > 0)
            {
                nonEmpty.Add(s);
            }
        }
        if (nonEmpty.Count == 0)
        {
            return;
        }

        var (min, max) = LineChartMath.ComputeRangeMulti(nonEmpty.ConvertAll(s => s.Values));
        double range = max - min;
        if (range <= 0d) range = 1d;

        const float marginTop = 18f;     // legend 공간 확보
        const float marginBottom = 6f;
        float plotHeight = dirtyRect.Height - marginTop - marginBottom;
        if (plotHeight <= 0f) plotHeight = dirtyRect.Height;

        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        foreach (var s in nonEmpty)
        {
            int n = s.Values.Count;
            if (n < 1) continue;

            float stepX = LineChartMath.ComputeStepX(n, dirtyRect.Width);
            canvas.StrokeColor = s.LineColor;
            canvas.StrokeSize = s.LineWidth;

            if (n == 1)
            {
                // 단일 sample — 점 1개.
                float x = dirtyRect.X + dirtyRect.Width - 4f;
                float yNorm = (float)((s.Values[0] - min) / range);
                float y = dirtyRect.Y + marginTop + (1f - yNorm) * plotHeight;
                canvas.FillColor = s.LineColor;
                canvas.FillCircle(x, y, s.LineWidth);
                continue;
            }

            var path = new PathF();
            for (int i = 0; i < n; i++)
            {
                float x = dirtyRect.X + i * stepX;
                float yNorm = (float)((s.Values[i] - min) / range);
                float y = dirtyRect.Y + marginTop + (1f - yNorm) * plotHeight;
                if (i == 0) path.MoveTo(x, y);
                else path.LineTo(x, y);
            }
            canvas.DrawPath(path);
        }

        if (ShowLegend)
        {
            DrawLegend(canvas, dirtyRect, nonEmpty);
        }
    }

    // Design Ref: §5.2 — 우상단 legend: "━ Label" 반복, 오른쪽 정렬.
    private static void DrawLegend(ICanvas canvas, RectF dirtyRect, IReadOnlyList<LineChartSeries> series)
    {
        const float legendY = 4f;
        const float lineWidth = 12f;     // 색상 라인 길이
        const float gapAfterLine = 4f;
        const float gapBetweenItems = 12f;
        const float fontSize = 11f;
        const float charWidthApprox = 6.5f;
        const float itemHeight = 14f;

        canvas.FontSize = fontSize;

        // 각 item 폭 계산 → 오른쪽 끝부터 거꾸로 배치.
        float right = dirtyRect.X + dirtyRect.Width - 2f;
        for (int idx = series.Count - 1; idx >= 0; idx--)
        {
            var s = series[idx];
            float labelWidth = s.Label.Length * charWidthApprox;
            float itemWidth = lineWidth + gapAfterLine + labelWidth;
            float itemLeft = right - itemWidth;

            // 색상 라인 (중앙선)
            canvas.StrokeColor = s.LineColor;
            canvas.StrokeSize = 2f;
            float lineY = dirtyRect.Y + legendY + itemHeight / 2f;
            canvas.DrawLine(itemLeft, lineY, itemLeft + lineWidth, lineY);

            // 라벨 텍스트
            canvas.FontColor = s.LineColor;
            canvas.DrawString(
                s.Label,
                itemLeft + lineWidth + gapAfterLine,
                dirtyRect.Y + legendY,
                labelWidth,
                itemHeight,
                HorizontalAlignment.Left,
                VerticalAlignment.Top);

            right = itemLeft - gapBetweenItems;
        }
    }
}
