using Microsoft.Maui.Graphics;

namespace VoiceTodo.Maui.Drawables;

/// <summary>
/// 环形进度：底层轨道圆环 + 上层进度弧（从 12 点起顺时针）。
/// 显式设置 <see cref="ProgressColor"/> 后用纯色描边（圆头端点）；
/// 否则回退 <see cref="ProgressBrush"/> 渐变环带填充。
/// 平滑时由调用方逐帧更新 <see cref="Progress"/> 并触发 GraphicsView.Invalidate()。
/// </summary>
public class RingDrawable : IDrawable
{
    /// <summary>底层轨道颜色（默认 TrackGray #E7EAEF）。</summary>
    public Color BackgroundColor { get; set; } = Color.FromRgb(0xE7, 0xEA, 0xEF);

    private Color _progressColor = Color.FromRgb(0xC6, 0x7A, 0x1C);
    private bool _progressColorSet;

    /// <summary>进度弧纯色（显式赋值后优先生效，覆盖渐变回退）。</summary>
    public Color ProgressColor
    {
        get => _progressColor;
        set { _progressColor = value; _progressColorSet = true; }
    }

    /// <summary>进度弧渐变画笔（Microsoft.Maui.Graphics.Paint，配合 SetFillPaint 使用；未显式设置 ProgressColor 时启用）。</summary>
    public Paint? ProgressBrush { get; set; }

    /// <summary>进度 0~1。</summary>
    public double Progress { get; set; }

    /// <summary>圆环线宽（即描边粗细）。</summary>
    public float StrokeThickness { get; set; } = 16f;

    /// <summary>起始角（度，0 在 3 点方向）；默认 -90 即 12 点方向。</summary>
    public float StartAngle { get; set; } = -90f;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float cx = dirtyRect.Center.X;
        float cy = dirtyRect.Center.Y;
        float radius = (MathF.Min(dirtyRect.Width, dirtyRect.Height) - StrokeThickness) / 2f;
        if (radius <= 0f) return;

        // 底层轨道
        canvas.SaveState();
        canvas.StrokeColor = BackgroundColor;
        canvas.StrokeSize = StrokeThickness;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawCircle(cx, cy, radius);
        canvas.RestoreState();

        float progress = Math.Clamp((float)Progress, 0f, 1f);
        if (progress <= 0f) return;

        float sweep = progress * 360f;
        float aStart = StartAngle;
        // 满圆时留 0.5° 缺口，避免起点/终点重合导致路径退化
        float aEnd = aStart + MathF.Min(sweep, 359.5f);
        float ro = radius + StrokeThickness / 2f;
        float ri = MathF.Max(0f, radius - StrokeThickness / 2f);

        canvas.SaveState();
        try
        {
            if (_progressColorSet || ProgressBrush == null)
            {
                // 显式 ProgressColor（或无渐变兜底）：纯色描边弧（自带圆头端点）
                canvas.StrokeColor = _progressColor;
                canvas.StrokeSize = StrokeThickness;
                canvas.StrokeLineCap = LineCap.Round;
                canvas.DrawArc(cx - radius, cy - radius, cx + radius, cy + radius, aStart, aEnd, true, true);
            }
            else
            {
                // 渐变进度弧：环带路径填充（Graphics 无 SetStrokePaint，故用 SetFillPaint 填充环带）
                canvas.SetFillPaint(ProgressBrush, dirtyRect);
                var path = new PathF();
                path.MoveTo(CenterOuter(aStart, cx, cy, ro));
                path.AddArc(cx - ro, cy - ro, cx + ro, cy + ro, aStart, aEnd, true);
                path.LineTo(CenterOuter(aEnd, cx, cy, ri));
                path.AddArc(cx - ri, cy - ri, cx + ri, cy + ri, aEnd, aStart, false);
                path.Close();
                canvas.FillPath(path);

                // 端点圆帽
                canvas.FillColor = (ProgressBrush as LinearGradientPaint)?.EndColor
                    ?? (ProgressBrush as SolidPaint)?.Color ?? _progressColor;
                canvas.FillCircle(CenterOuter(aEnd, cx, cy, radius), StrokeThickness / 2f);
            }
        }
        finally
        {
            canvas.RestoreState();
        }

        static PointF CenterOuter(float deg, float cx, float cy, float r)
            => new(cx + r * CosDeg(deg), cy + r * SinDeg(deg));
        static float CosDeg(float deg) => MathF.Cos(deg * MathF.PI / 180f);
        static float SinDeg(float deg) => MathF.Sin(deg * MathF.PI / 180f);
    }
}
