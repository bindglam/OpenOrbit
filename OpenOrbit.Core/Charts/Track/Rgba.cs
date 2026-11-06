using System.Globalization;

namespace OpenOrbit.Core.Charts.Track;

/// <summary>0~1 범위의 RGBA 색.</summary>
public readonly record struct Rgba(float R, float G, float B, float A = 1)
{
    public static readonly Rgba White = new(1, 1, 1);

    /// <summary>"rrggbb" 또는 "rrggbbaa" (앞의 # 은 있어도 된다). 읽을 수 없으면 fallback.</summary>
    public static Rgba Parse(string? hex, Rgba fallback)
    {
        if (hex is null)
            return fallback;
        var s = hex.AsSpan().TrimStart('#');
        if (s.Length is not (6 or 8) || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return fallback;
        if (s.Length == 6)
            v = (v << 8) | 0xff;
        return new Rgba((v >> 24 & 0xff) / 255f, (v >> 16 & 0xff) / 255f, (v >> 8 & 0xff) / 255f, (v & 0xff) / 255f);
    }

    public static Rgba Lerp(Rgba a, Rgba b, double t)
    {
        var f = (float)t;
        return new Rgba(a.R + (b.R - a.R) * f, a.G + (b.G - a.G) * f, a.B + (b.B - a.B) * f, a.A + (b.A - a.A) * f);
    }

    /// <summary>h, s, v 는 0~1.</summary>
    public static Rgba FromHsv(double h, double s, double v, float a = 1)
    {
        h = (h % 1 + 1) % 1 * 6;
        var i = (int)h;
        var f = h - i;
        var p = v * (1 - s);
        var q = v * (1 - s * f);
        var t = v * (1 - s * (1 - f));
        var (r, g, b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return new Rgba((float)r, (float)g, (float)b, a);
    }

    /// <summary>(h, s, v), 모두 0~1. 알파는 무시한다.</summary>
    public (double H, double S, double V) ToHsv()
    {
        double r = R, g = G, b = B;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        var s = max <= 0 ? 0 : d / max;
        if (d <= 0)
            return (0, s, max);
        var h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return ((h / 6 % 1 + 1) % 1, s, max);
    }
}
