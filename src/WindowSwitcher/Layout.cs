using WindowSwitcher.Native;

namespace WindowSwitcher;

/// <summary>Popup sizes in physical pixels for one display scale.</summary>
internal readonly record struct Metrics(double Scale)
{
    int S(double v) => (int)Math.Round(v * Scale);

    public int Padding => S(12);
    public int GapX => S(8);
    public int GapY => S(8);
    public int Inset => S(6);
    public int HeaderHeight => S(24);
    public int HeaderGap => S(4);
    public int HeaderIcon => S(16);
    public int HeaderTextGap => S(6);
    public int PlaceholderIcon => S(48);
    public int MinImageWidth => S(72);
    public int MinImageHeight => S(48);
    public int PointerOffset => S(16);
    public int ScreenMargin => S(8);
    public int Radius => S(8);
    public int Border => Math.Max(2, S(2));
    public int FontHeight => S(12);
}

internal sealed class TileLayout
{
    public required RECT Tile { get; init; }
    public required RECT Header { get; init; }
    public required RECT ImageBox { get; init; }
}

internal sealed class PopupLayout
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int ImageHeight { get; init; }
    public required List<TileLayout> Tiles { get; init; }
}

/// <summary>
/// Rows of tiles, one row per program. Every thumbnail in the popup has the same height; when the
/// popup would not fit the work area, that height shrinks (down to a floor) until it does.
/// </summary>
internal static class Layout
{
    const double MinAspect = 0.5;
    const double MaxAspect = 2.5;

    /// <param name="aspects">Width/height of each window, per row.</param>
    public static PopupLayout Compute(IReadOnlyList<IReadOnlyList<double>> aspects, Metrics m, int imageHeight, int maxWidth, int maxHeight)
    {
        int height = Math.Max(m.MinImageHeight, imageHeight);
        if (!Fits(aspects, m, height, maxWidth, maxHeight))
        {
            int low = m.MinImageHeight, high = height;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (Fits(aspects, m, mid, maxWidth, maxHeight)) low = mid;
                else high = mid - 1;
            }
            height = low;
        }
        return Build(aspects, m, height);
    }

    static int ImageWidth(double aspect, int height, Metrics m) =>
        Math.Max(m.MinImageWidth, (int)Math.Round(Math.Clamp(aspect, MinAspect, MaxAspect) * height));

    static int TileHeight(Metrics m, int imageHeight) =>
        2 * m.Inset + m.HeaderHeight + m.HeaderGap + imageHeight;

    static bool Fits(IReadOnlyList<IReadOnlyList<double>> aspects, Metrics m, int imageHeight, int maxWidth, int maxHeight)
    {
        var (w, h) = Size(aspects, m, imageHeight);
        return w <= maxWidth && h <= maxHeight;
    }

    static (int Width, int Height) Size(IReadOnlyList<IReadOnlyList<double>> aspects, Metrics m, int imageHeight)
    {
        int widest = 0;
        foreach (var row in aspects)
        {
            int w = 0;
            foreach (double a in row) w += ImageWidth(a, imageHeight, m) + 2 * m.Inset;
            w += m.GapX * Math.Max(0, row.Count - 1);
            widest = Math.Max(widest, w);
        }
        int rows = aspects.Count;
        int height = rows * TileHeight(m, imageHeight) + m.GapY * Math.Max(0, rows - 1);
        return (widest + 2 * m.Padding, height + 2 * m.Padding);
    }

    static PopupLayout Build(IReadOnlyList<IReadOnlyList<double>> aspects, Metrics m, int imageHeight)
    {
        var tiles = new List<TileLayout>();
        int tileHeight = TileHeight(m, imageHeight);
        int y = m.Padding;
        foreach (var row in aspects)
        {
            int x = m.Padding;
            foreach (double a in row)
            {
                int imageWidth = ImageWidth(a, imageHeight, m);
                var tile = new RECT(x, y, x + imageWidth + 2 * m.Inset, y + tileHeight);
                int headerTop = y + m.Inset;
                var header = new RECT(tile.Left + m.Inset, headerTop, tile.Right - m.Inset, headerTop + m.HeaderHeight);
                int imageTop = header.Bottom + m.HeaderGap;
                var image = new RECT(tile.Left + m.Inset, imageTop, tile.Right - m.Inset, imageTop + imageHeight);
                tiles.Add(new TileLayout { Tile = tile, Header = header, ImageBox = image });
                x = tile.Right + m.GapX;
            }
            y += tileHeight + m.GapY;
        }
        var (width, height) = Size(aspects, m, imageHeight);
        return new PopupLayout { Width = width, Height = height, ImageHeight = imageHeight, Tiles = tiles };
    }

    /// <summary>The largest rectangle with the given aspect ratio centered in a box (may scale up).</summary>
    public static RECT FitCentered(RECT box, int sourceWidth, int sourceHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0) return box;
        double scale = Math.Min((double)box.Width / sourceWidth, (double)box.Height / sourceHeight);
        int w = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        int h = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        int x = box.Left + (box.Width - w) / 2;
        int y = box.Top + (box.Height - h) / 2;
        return new RECT(x, y, x + w, y + h);
    }
}
