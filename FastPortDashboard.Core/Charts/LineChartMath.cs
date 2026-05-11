namespace FastPortDashboard.Core.Charts;

// Design Ref: §3 (dashboard-chart-graphicsview-migration) —
// LineChartDrawable의 순수 수학 부분을 Core로 분리하여 단위 테스트 가능하게 한다.
// MAUI 의존성(IDrawable, ICanvas, RectF) 없음.
public static class LineChartMath
{
    // Design Ref: §6 — 빈 배열/단일 값/동일 값에 대한 안전 범위 계산.
    public static (double Min, double Max) ComputeRange(IReadOnlyList<double> values)
    {
        if (values is null || values.Count == 0)
        {
            return (0d, 1d);
        }

        double min = values[0];
        double max = values[0];
        for (int i = 1; i < values.Count; i++)
        {
            double v = values[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }

        if (min == max)
        {
            // 동일 값일 때 0 division 방지: ±0.5 padding.
            return (min - 0.5d, max + 0.5d);
        }

        return (min, max);
    }

    // Design Ref: §11.2 — 마지막 sample x 위치 계산 (점 1개일 때는 오른쪽 끝에 표시).
    public static float ComputeStepX(int count, float width)
    {
        if (count <= 1 || width <= 0f) return 0f;
        return width / (count - 1);
    }
}
