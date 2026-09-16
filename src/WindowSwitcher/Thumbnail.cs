namespace WindowSwitcher;

/// <summary>A captured window image: premultiplied BGRA, top-down rows.</summary>
internal sealed class CapturedImage(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;
}

internal static class Thumbnail
{
    /// <summary>The largest size with the source's aspect ratio that fits the box, never larger than the source.</summary>
    public static (int Width, int Height) Fit(int width, int height, int boxWidth, int boxHeight)
    {
        double scale = Math.Min(1.0, Math.Min((double)boxWidth / width, (double)boxHeight / height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>
    /// Area-average downscale (separable box filter with fractional coverage). The destination is never
    /// larger than the source on either axis.
    /// </summary>
    public static byte[] Resample(byte[] src, int sw, int sh, int dw, int dh)
    {
        if (sw == dw && sh == dh) return src;

        var (xStart, xCount, xWeights) = Contributions(sw, dw);
        var (yStart, yCount, yWeights) = Contributions(sh, dh);

        // Horizontal pass: values scaled by 1024.
        var tmp = new int[dw * sh * 4];
        for (int y = 0; y < sh; y++)
        {
            int srcRow = y * sw * 4;
            int dstRow = y * dw * 4;
            for (int x = 0; x < dw; x++)
            {
                int b = 0, g = 0, r = 0, a = 0;
                int wi = x * xCount.MaxPerSample;
                for (int k = 0; k < xCount.Counts[x]; k++)
                {
                    int w = xWeights[wi + k];
                    int s = srcRow + (xStart[x] + k) * 4;
                    b += src[s] * w;
                    g += src[s + 1] * w;
                    r += src[s + 2] * w;
                    a += src[s + 3] * w;
                }
                int d = dstRow + x * 4;
                tmp[d] = b; tmp[d + 1] = g; tmp[d + 2] = r; tmp[d + 3] = a;
            }
        }

        // Vertical pass: scaled by 1024 again, so shift by 20 with rounding.
        var dst = new byte[dw * dh * 4];
        for (int y = 0; y < dh; y++)
        {
            int wi = y * yCount.MaxPerSample;
            int dstRow = y * dw * 4;
            for (int x = 0; x < dw; x++)
            {
                long b = 0, g = 0, r = 0, a = 0;
                for (int k = 0; k < yCount.Counts[y]; k++)
                {
                    int w = yWeights[wi + k];
                    int s = ((yStart[y] + k) * dw + x) * 4;
                    b += (long)tmp[s] * w;
                    g += (long)tmp[s + 1] * w;
                    r += (long)tmp[s + 2] * w;
                    a += (long)tmp[s + 3] * w;
                }
                int d = dstRow + x * 4;
                dst[d] = (byte)Math.Min(255, (b + (1 << 19)) >> 20);
                dst[d + 1] = (byte)Math.Min(255, (g + (1 << 19)) >> 20);
                dst[d + 2] = (byte)Math.Min(255, (r + (1 << 19)) >> 20);
                dst[d + 3] = (byte)Math.Min(255, (a + (1 << 19)) >> 20);
            }
        }
        return dst;
    }

    readonly record struct CountTable(int[] Counts, int MaxPerSample);

    /// <summary>Per destination sample: first source index, how many source samples, and 10-bit weights summing to 1024.</summary>
    static (int[] Start, CountTable Count, int[] Weights) Contributions(int srcLen, int dstLen)
    {
        double scale = (double)srcLen / dstLen;
        int max = (int)Math.Ceiling(scale) + 1;
        var start = new int[dstLen];
        var counts = new int[dstLen];
        var weights = new int[dstLen * max];

        for (int i = 0; i < dstLen; i++)
        {
            double a = i * scale;
            double b = Math.Min(srcLen, (i + 1) * scale);
            int first = (int)Math.Floor(a);
            int last = Math.Min(srcLen - 1, (int)Math.Ceiling(b) - 1);
            int n = Math.Min(max, last - first + 1);
            start[i] = first;
            counts[i] = n;

            int sum = 0, biggest = 0;
            for (int k = 0; k < n; k++)
            {
                int j = first + k;
                double overlap = Math.Min(b, j + 1) - Math.Max(a, j);
                int w = (int)Math.Round(overlap / (b - a) * 1024);
                weights[i * max + k] = w;
                sum += w;
                if (w > weights[i * max + biggest]) biggest = k;
            }
            weights[i * max + biggest] += 1024 - sum;
        }
        return (start, new CountTable(counts, max), weights);
    }
}
