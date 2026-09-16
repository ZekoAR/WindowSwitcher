using System.Collections.Concurrent;
using System.Text;
using Windows.UI.ViewManagement;
using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>
/// The thumbnail popup: a topmost, never-activated window painted with GDI from a back buffer.
/// Captured thumbnails arrive asynchronously and are painted into their tiles as they come; minimized
/// windows show DWM's retained image instead, drawn by DWM into the tile.
/// </summary>
internal sealed class Popup
{
    const string ClassName = "WindowSwitcher.Popup";
    const int CaptureTimeoutMs = 1000;
    const int PS_INSIDEFRAME = 6;
    const uint DEFAULT_CHARSET = 1;

    static readonly uint PanelColor = Rgb(32, 32, 32);
    static readonly uint HoverColor = Rgb(58, 58, 58);
    static readonly uint PlaceholderColor = Rgb(46, 46, 46);
    static readonly uint TextColor = Rgb(230, 230, 230);
    static readonly uint FrameColor = Rgb(64, 64, 64);
    static readonly uint FallbackAccent = Rgb(96, 165, 250);

    static Popup? s_instance;

    readonly nint _hwnd;
    readonly nint _notify;
    readonly nint _panelBrush;
    readonly nint _hoverBrush;
    readonly nint _placeholderBrush;
    readonly ConcurrentQueue<CaptureResult> _results = new();
    readonly string? _gateLayoutPath = Environment.GetEnvironmentVariable("WINDOWSWITCHER_GATE_LAYOUT");
    UISettings? _uiSettings;
    bool _accentFailureLogged;

    bool _open;
    int _generation;
    int _left, _top, _width, _height;
    Metrics _metrics = new(1);
    List<Tile> _tiles = [];
    int _hover = -1;
    uint _accent;
    nint _accentPen;
    int _accentPenWidth;
    CancellationTokenSource? _cancel;

    nint _memDc;
    nint _bitmapDc;
    nint _buffer;
    nint _oldBuffer;
    nint _font;
    int _fontHeight;

    sealed class Tile(WindowInfo window, string? exe)
    {
        public WindowInfo Window { get; } = window;
        public string? Exe { get; } = exe;
        public TileLayout Layout { get; set; } = null!;
        public nint HeaderIcon { get; set; }
        public nint PlaceholderIcon { get; set; }
        public nint Bitmap { get; set; }
        public int BitmapWidth { get; set; }
        public int BitmapHeight { get; set; }
        public nint Thumbnail { get; set; }
        public SIZE ThumbnailSource { get; set; }
    }

    readonly record struct CaptureResult(int Generation, int Index, CapturedImage? Image, string? Error);

    public unsafe Popup(nint instance, nint notify)
    {
        s_instance = this;
        _notify = notify;
        _panelBrush = Gdi32.CreateSolidBrush(PanelColor);
        _hoverBrush = Gdi32.CreateSolidBrush(HoverColor);
        _placeholderBrush = Gdi32.CreateSolidBrush(PlaceholderColor);

        fixed (char* cls = ClassName)
        fixed (char* title = "WindowSwitcher")
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = CS_DROPSHADOW,
                lpfnWndProc = &WndProc,
                hInstance = instance,
                hCursor = User32.LoadCursorW(0, IDC_ARROW),
                lpszClassName = cls,
            };
            if (User32.RegisterClassExW(&wc) == 0)
                throw new InvalidOperationException($"RegisterClassEx(popup) failed: {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
            _hwnd = User32.CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, cls, title, WS_POPUP,
                0, 0, 1, 1, 0, 0, instance, 0);
        }
        if (_hwnd == 0)
            throw new InvalidOperationException($"CreateWindowEx(popup) failed: {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");

        int corner = DWMWCP_ROUND;
        Dwm.DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(int));
        uint frame = FrameColor;
        Dwm.DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, &frame, sizeof(uint));
    }

    public bool IsOpen => _open;

    public void Open(int x, int y, Settings settings, Task<CaptureService> capture)
    {
        if (_open) Hide();

        nint monitor = User32.MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        var (bounds, work, dpi) = Monitors.Describe(monitor);
        _metrics = new Metrics(dpi / 96.0);
        Metrics m = _metrics;
        EnsureFont(m.FontHeight);
        UpdateAccent(m);

        var groups = WindowList.ForMonitor(monitor);
        var aspects = new List<IReadOnlyList<double>>(groups.Count);
        foreach (var group in groups)
        {
            var row = new List<double>(group.Windows.Count);
            foreach (var window in group.Windows)
            {
                var tile = new Tile(window, group.ExePath);
                double aspect = (double)window.Bounds.Width / window.Bounds.Height;
                if (window.Minimized && RegisterThumbnail(tile) && tile.ThumbnailSource.cx > 0)
                    aspect = (double)tile.ThumbnailSource.cx / tile.ThumbnailSource.cy;
                row.Add(aspect);
                _tiles.Add(tile);
            }
            aspects.Add(row);
        }

        int maxWidth = work.Width - 2 * m.ScreenMargin;
        int maxHeight = work.Height - 2 * m.ScreenMargin;
        int imageHeight = (int)Math.Round(settings.ThumbnailHeight * m.Scale);
        int layoutWidth, layoutHeight;
        if (_tiles.Count == 0)
        {
            layoutWidth = (int)Math.Round(260 * m.Scale);
            layoutHeight = (int)Math.Round(48 * m.Scale);
        }
        else
        {
            PopupLayout layout = Layout.Compute(aspects, m, imageHeight, maxWidth, maxHeight);
            for (int i = 0; i < _tiles.Count; i++)
            {
                Tile tile = _tiles[i];
                tile.Layout = layout.Tiles[i];
                tile.HeaderIcon = Icons.ForHeader(tile.Window.Hwnd, tile.Exe, m.HeaderIcon);
                tile.PlaceholderIcon = Icons.ForPlaceholder(tile.Window.Hwnd, tile.Exe, m.PlaceholderIcon);
            }
            layoutWidth = layout.Width;
            layoutHeight = layout.Height;
        }
        _width = Math.Min(layoutWidth, maxWidth);
        _height = Math.Min(layoutHeight, maxHeight);

        // A little right of the pointer, centered on it vertically; moved inside the work area if needed,
        // in which case the pointer follows to the same spot relative to the popup.
        int wantLeft = x + m.PointerOffset;
        int wantTop = y - _height / 2;
        _left = Math.Max(work.Left + m.ScreenMargin, Math.Min(wantLeft, work.Right - m.ScreenMargin - _width));
        _top = Math.Max(work.Top + m.ScreenMargin, Math.Min(wantTop, work.Bottom - m.ScreenMargin - _height));
        bool moved = _left != wantLeft || _top != wantTop;
        int pointerX = x, pointerY = y;
        if (moved)
        {
            pointerX = Math.Clamp(_left - m.PointerOffset, bounds.Left, bounds.Right - 1);
            pointerY = Math.Clamp(_top + _height / 2, bounds.Top, bounds.Bottom - 1);
            User32.SetCursorPos(pointerX, pointerY);
            InputHook.SetLastPoint(pointerX, pointerY);
        }

        CreateBuffer(_width, _height);
        _hover = -1;
        _open = true;
        RenderAll();
        User32.SetWindowPos(_hwnd, HWND_TOPMOST, _left, _top, _width, _height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Invalidate(new RECT(0, 0, _width, _height));
        foreach (var tile in _tiles)
            if (tile.Thumbnail != 0) ShowThumbnail(tile);

        Hover(pointerX, pointerY);
        StartCaptures(capture);
        WriteGateLayout(bounds, work, dpi, pointerX, pointerY, moved);
    }

    public void Hover(int x, int y)
    {
        if (!_open) return;
        int hit = HitTest(x, y);
        if (hit == _hover) return;
        int previous = _hover;
        _hover = hit;
        if (previous >= 0) RedrawTile(previous);
        if (hit >= 0) RedrawTile(hit);
    }

    /// <summary>Hides the popup and returns the window under (x, y), or 0.</summary>
    public nint Close(int x, int y)
    {
        if (!_open) return 0;
        int hit = HitTest(x, y);
        nint target = hit >= 0 ? _tiles[hit].Window.Hwnd : 0;
        Hide();
        GateLine($"closed {x} {y} hit {target:X}");
        return target;
    }

    public void Hide()
    {
        _cancel?.Cancel();
        _cancel = null;
        _generation++;
        bool wasOpen = _open;
        _open = false;
        if (wasOpen) User32.ShowWindow(_hwnd, SW_HIDE);
        foreach (var tile in _tiles)
        {
            if (tile.Thumbnail != 0) Dwm.DwmUnregisterThumbnail(tile.Thumbnail);
            if (tile.Bitmap != 0) Gdi32.DeleteObject(tile.Bitmap);
        }
        _tiles = [];
        _hover = -1;
        while (_results.TryDequeue(out _)) { }
        ReleaseBuffer();
    }

    /// <summary>Paints the thumbnails that finished capturing since the last call.</summary>
    public void DrainResults()
    {
        while (_results.TryDequeue(out var result))
        {
            if (!_open || result.Generation != _generation || result.Index >= _tiles.Count) continue;
            Tile tile = _tiles[result.Index];
            if (result.Image is null)
            {
                Log.Write($"capture failed for \"{tile.Window.Title}\" ({tile.Window.Hwnd:X}): {result.Error}");
                GateLine($"capture-failed {tile.Window.Hwnd:X} {result.Error}");
                continue;
            }
            tile.Bitmap = CreateBitmap(result.Image);
            tile.BitmapWidth = result.Image.Width;
            tile.BitmapHeight = result.Image.Height;
            GateLine($"captured {tile.Window.Hwnd:X} {result.Image.Width}x{result.Image.Height}");
            RedrawTile(result.Index);
        }
    }

    public void GateLine(string line)
    {
        if (_gateLayoutPath is null) return;
        try
        {
            File.AppendAllText(_gateLayoutPath, line + Environment.NewLine);
        }
        catch
        {
        }
    }

    int HitTest(int x, int y)
    {
        int cx = x - _left, cy = y - _top;
        if (cx < 0 || cy < 0 || cx >= _width || cy >= _height) return -1;
        for (int i = 0; i < _tiles.Count; i++)
            if (_tiles[i].Layout.Tile.Contains(cx, cy)) return i;
        return -1;
    }

    void StartCaptures(Task<CaptureService> capture)
    {
        _cancel = new CancellationTokenSource();
        CancellationToken token = _cancel.Token;
        int generation = _generation;
        for (int i = 0; i < _tiles.Count; i++)
        {
            Tile tile = _tiles[i];
            if (tile.Window.Minimized) continue;
            int index = i;
            nint target = tile.Window.Hwnd;
            RECT box = tile.Layout.ImageBox;
            _ = Task.Run(async () =>
            {
                CapturedImage? image = null;
                string? error = null;
                try
                {
                    CaptureService service = await capture.ConfigureAwait(false);
                    image = await service.CaptureAsync(target, box.Width, box.Height, CaptureTimeoutMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    error = $"{e.GetType().Name}: {e.Message}";
                }
                _results.Enqueue(new CaptureResult(generation, index, image, error));
                User32.PostMessageW(_notify, App.WM_THUMBNAIL, 0, 0);
            });
        }
    }

    bool RegisterThumbnail(Tile tile)
    {
        if (Dwm.DwmRegisterThumbnail(_hwnd, tile.Window.Hwnd, out nint thumbnail) < 0 || thumbnail == 0)
            return false;
        tile.Thumbnail = thumbnail;
        if (Dwm.DwmQueryThumbnailSourceSize(thumbnail, out SIZE size) >= 0 && size.cx > 0 && size.cy > 0)
            tile.ThumbnailSource = size;
        return true;
    }

    unsafe void ShowThumbnail(Tile tile)
    {
        SIZE source = tile.ThumbnailSource.cx > 0
            ? tile.ThumbnailSource
            : new SIZE { cx = tile.Window.Bounds.Width, cy = tile.Window.Bounds.Height };
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = Layout.FitCentered(tile.Layout.ImageBox, source.cx, source.cy),
            opacity = 255,
            fVisible = 1,
            fSourceClientAreaOnly = 0,
        };
        int hr = Dwm.DwmUpdateThumbnailProperties(tile.Thumbnail, &props);
        if (hr < 0) Log.Write($"DwmUpdateThumbnailProperties failed for {tile.Window.Hwnd:X}: 0x{hr:X8}");
    }

    // ---- painting ----

    void RedrawTile(int index)
    {
        RenderTile(index);
        Invalidate(_tiles[index].Layout.Tile);
    }

    unsafe void Invalidate(RECT r) => User32.InvalidateRect(_hwnd, &r, false);

    unsafe void Paint(nint hwnd)
    {
        PAINTSTRUCT ps;
        nint hdc = User32.BeginPaint(hwnd, &ps);
        if (_open && _memDc != 0)
            Gdi32.BitBlt(hdc, 0, 0, _width, _height, _memDc, 0, 0, SRCCOPY);
        User32.EndPaint(hwnd, &ps);
    }

    unsafe void RenderAll()
    {
        var all = new RECT(0, 0, _width, _height);
        User32.FillRect(_memDc, &all, _panelBrush);
        if (_tiles.Count == 0)
        {
            DrawText("No windows on this monitor", all, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
            return;
        }
        for (int i = 0; i < _tiles.Count; i++)
            RenderTile(i);
    }

    unsafe void RenderTile(int index)
    {
        Tile tile = _tiles[index];
        TileLayout l = tile.Layout;
        Metrics m = _metrics;

        RECT area = l.Tile;
        User32.FillRect(_memDc, &area, _panelBrush);
        if (index == _hover)
        {
            nint oldPen = Gdi32.SelectObject(_memDc, _accentPen);
            nint oldBrush = Gdi32.SelectObject(_memDc, _hoverBrush);
            Gdi32.RoundRect(_memDc, area.Left, area.Top, area.Right, area.Bottom, 2 * m.Radius, 2 * m.Radius);
            Gdi32.SelectObject(_memDc, oldBrush);
            Gdi32.SelectObject(_memDc, oldPen);
        }

        RECT header = l.Header;
        int icon = m.HeaderIcon;
        User32.DrawIconEx(_memDc, header.Left, header.Top + (header.Height - icon) / 2, tile.HeaderIcon, icon, icon, 0, 0, DI_NORMAL);
        var text = new RECT(header.Left + icon + m.HeaderTextGap, header.Top, header.Right, header.Bottom);
        DrawText(tile.Window.Title, text, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS | DT_NOPREFIX);

        RECT box = l.ImageBox;
        if (tile.Bitmap != 0)
        {
            int x = box.Left + (box.Width - tile.BitmapWidth) / 2;
            int y = box.Top + (box.Height - tile.BitmapHeight) / 2;
            nint old = Gdi32.SelectObject(_bitmapDc, tile.Bitmap);
            Gdi32.GdiAlphaBlend(_memDc, x, y, tile.BitmapWidth, tile.BitmapHeight,
                _bitmapDc, 0, 0, tile.BitmapWidth, tile.BitmapHeight, Gdi32.Blend(255, perPixel: true));
            Gdi32.SelectObject(_bitmapDc, old);
            return;
        }

        // Placeholder until the capture arrives, and under DWM's image for a minimized window.
        RECT place = tile.Thumbnail != 0
            ? Layout.FitCentered(box,
                tile.ThumbnailSource.cx > 0 ? tile.ThumbnailSource.cx : tile.Window.Bounds.Width,
                tile.ThumbnailSource.cy > 0 ? tile.ThumbnailSource.cy : tile.Window.Bounds.Height)
            : box;
        nint nullPen = Gdi32.GetStockObject(NULL_PEN);
        nint prevPen = Gdi32.SelectObject(_memDc, nullPen);
        nint prevBrush = Gdi32.SelectObject(_memDc, _placeholderBrush);
        Gdi32.RoundRect(_memDc, place.Left, place.Top, place.Right + 1, place.Bottom + 1, m.Radius, m.Radius);
        Gdi32.SelectObject(_memDc, prevBrush);
        Gdi32.SelectObject(_memDc, prevPen);

        int size = Math.Min(m.PlaceholderIcon, (int)(Math.Min(place.Width, place.Height) * 0.6));
        if (size > 0)
            User32.DrawIconEx(_memDc, place.Left + (place.Width - size) / 2, place.Top + (place.Height - size) / 2,
                tile.PlaceholderIcon, size, size, 0, 0, DI_NORMAL);
    }

    unsafe void DrawText(string s, RECT r, uint format)
    {
        nint old = Gdi32.SelectObject(_memDc, _font);
        Gdi32.SetBkMode(_memDc, TRANSPARENT);
        Gdi32.SetTextColor(_memDc, TextColor);
        fixed (char* p = s)
            User32.DrawTextW(_memDc, p, s.Length, &r, format);
        Gdi32.SelectObject(_memDc, old);
    }

    void EnsureFont(int height)
    {
        if (_font != 0 && _fontHeight == height) return;
        if (_font != 0) Gdi32.DeleteObject(_font);
        _font = Gdi32.CreateFontW(-height, 0, 0, 0, FW_NORMAL, 0, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, "Segoe UI");
        _fontHeight = height;
    }

    void UpdateAccent(Metrics m)
    {
        uint accent = ReadAccent();
        if (_accentPen != 0 && accent == _accent && _accentPenWidth == m.Border) return;
        if (_accentPen != 0) Gdi32.DeleteObject(_accentPen);
        _accentPen = Gdi32.CreatePen(PS_INSIDEFRAME, m.Border, accent);
        _accent = accent;
        _accentPenWidth = m.Border;
    }

    uint ReadAccent()
    {
        try
        {
            _uiSettings ??= new UISettings();
            var c = _uiSettings.GetColorValue(UIColorType.AccentLight2);
            return Rgb(c.R, c.G, c.B);
        }
        catch (Exception e)
        {
            if (!_accentFailureLogged) Log.Error("reading the accent color", e);
            _accentFailureLogged = true;
            return FallbackAccent;
        }
    }

    unsafe void CreateBuffer(int width, int height)
    {
        ReleaseBuffer();
        _memDc = Gdi32.CreateCompatibleDC(0);
        _bitmapDc = Gdi32.CreateCompatibleDC(0);
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
            },
        };
        _buffer = Gdi32.CreateDIBSection(0, &bmi, DIB_RGB_COLORS, out _, 0, 0);
        _oldBuffer = Gdi32.SelectObject(_memDc, _buffer);
    }

    void ReleaseBuffer()
    {
        if (_memDc != 0)
        {
            Gdi32.SelectObject(_memDc, _oldBuffer);
            Gdi32.DeleteDC(_memDc);
            _memDc = 0;
        }
        if (_bitmapDc != 0)
        {
            Gdi32.DeleteDC(_bitmapDc);
            _bitmapDc = 0;
        }
        if (_buffer != 0)
        {
            Gdi32.DeleteObject(_buffer);
            _buffer = 0;
        }
    }

    static unsafe nint CreateBitmap(CapturedImage image)
    {
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = image.Width,
                biHeight = -image.Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
            },
        };
        nint bitmap = Gdi32.CreateDIBSection(0, &bmi, DIB_RGB_COLORS, out byte* bits, 0, 0);
        if (bitmap == 0) return 0;
        fixed (byte* src = image.Pixels)
            Buffer.MemoryCopy(src, bits, image.Pixels.Length, image.Pixels.Length);
        return bitmap;
    }

    void WriteGateLayout(RECT bounds, RECT work, uint dpi, int pointerX, int pointerY, bool moved)
    {
        if (_gateLayoutPath is null) return;
        var sb = new StringBuilder();
        sb.AppendLine($"popup {_left} {_top} {_left + _width} {_top + _height}");
        sb.AppendLine($"monitor {bounds.Left} {bounds.Top} {bounds.Right} {bounds.Bottom}");
        sb.AppendLine($"work {work.Left} {work.Top} {work.Right} {work.Bottom}");
        sb.AppendLine($"dpi {dpi} offset {_metrics.PointerOffset}");
        sb.AppendLine($"pointer {pointerX} {pointerY} moved {(moved ? 1 : 0)}");
        foreach (var tile in _tiles)
        {
            RECT r = tile.Layout.Tile;
            sb.AppendLine($"tile {tile.Window.Hwnd:X} {r.Left + _left} {r.Top + _top} {r.Right + _left} {r.Bottom + _top} " +
                $"{(tile.Window.Minimized ? 1 : 0)} {(tile.Thumbnail != 0 ? 1 : 0)} {tile.Window.Title}");
        }
        try
        {
            File.WriteAllText(_gateLayoutPath, sb.ToString());
        }
        catch
        {
        }
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_PAINT:
                    s_instance?.Paint(hwnd);
                    return 0;
                case WM_ERASEBKGND:
                    return 1;
                case WM_MOUSEACTIVATE:
                    return MA_NOACTIVATE;
                case WM_DPICHANGED:
                    return 0; // sized for the target monitor already
                case WM_CONTEXTMENU:
                    return 0;
                case WM_RBUTTONUP:
                    // Only reaches the popup if the hook did not swallow the release.
                    App.EndGestureWithoutHook();
                    return 0;
                case WM_LBUTTONUP:
                    // Closes without switching; the hook still swallows the right-button release.
                    s_instance?.Hide();
                    return 0;
            }
        }
        catch (Exception e)
        {
            Log.Error($"popup message 0x{msg:X}", e);
        }
        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
