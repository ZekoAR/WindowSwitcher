using System.Collections.Concurrent;
using System.Text;
using Windows.UI.ViewManagement;
using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>
/// The thumbnail popup: a topmost, never-activated window painted from a premultiplied back buffer.
/// The background is transparent; only the tiles are drawn, each on its own opaque plate. Captured
/// thumbnails arrive asynchronously and are painted into their tiles as they come; minimized windows
/// show DWM's retained image instead, drawn by DWM into the tile.
/// </summary>
internal sealed class Popup
{
    const string ClassName = "WindowSwitcher.Popup";
    const int CaptureTimeoutMs = 1000;
    const uint DEFAULT_CHARSET = 1;

    static readonly uint PlateColor = Rgb(32, 32, 32);
    static readonly uint OutlineColor = Rgb(140, 140, 140);
    static readonly uint HoverColor = Rgb(58, 58, 58);
    static readonly uint PlaceholderColor = Rgb(46, 46, 46);
    static readonly uint TextColor = Rgb(230, 230, 230);
    static readonly uint FallbackAccent = Rgb(96, 165, 250);

    static Popup? s_instance;

    readonly nint _hwnd;
    readonly nint _notify;
    readonly ConcurrentQueue<CaptureResult> _results = new();
    readonly WindowOrder _order = new();
    readonly Peek _peek;
    readonly Dimmer? _dimmer;
    readonly string? _gateLayoutPath = Environment.GetEnvironmentVariable("WINDOWSWITCHER_GATE_LAYOUT");
    UISettings? _uiSettings;
    bool _accentFailureLogged;

    bool _open;
    bool _allMonitors;
    int _generation;
    int _left, _top, _width, _height;
    Metrics _metrics = new(1);
    List<Tile> _tiles = [];
    int _hover = -1;
    uint _accent;
    CancellationTokenSource? _cancel;

    nint _memDc;
    nint _bitmapDc;
    nint _buffer;
    nint _bits; // the back buffer's pixels: premultiplied BGRA, top-down, _width x _height
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

        fixed (char* cls = ClassName)
        fixed (char* title = "WindowSwitcher")
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
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

        // No window outline: the tiles are the only visible shapes.
        int corner = DWMWCP_DONOTROUND;
        Dwm.DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(int));
        uint noBorder = DWMWA_COLOR_NONE;
        Dwm.DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, &noBorder, sizeof(uint));

        // Per-pixel transparency on a normal window: blur-behind with an empty region makes DWM compose
        // the window with its own alpha channel and blur nothing. A layered window was not used because
        // it is composed differently, and the DWM thumbnails for minimized windows are drawn into this one.
        nint region = Gdi32.CreateRectRgn(0, 0, -1, -1);
        var blur = new DWM_BLURBEHIND { dwFlags = DWM_BB_ENABLE | DWM_BB_BLURREGION, fEnable = 1, hRgnBlur = region };
        int hr = Dwm.DwmEnableBlurBehindWindow(_hwnd, &blur);
        Gdi32.DeleteObject(region);
        if (hr < 0) Log.Write($"DwmEnableBlurBehindWindow failed: 0x{hr:X8}; the popup background will not be transparent");

        _peek = new Peek(instance);
        try
        {
            _dimmer = new Dimmer(instance);
        }
        catch (Exception e)
        {
            Log.Error("setting up the dimming layer (the screen will not be dimmed)", e);
        }
    }

    public bool IsOpen => _open;

    /// <summary>The highlight color (the Windows accent color), as read at the last opening.</summary>
    public uint Accent => _accent;

    /// <param name="allMonitors">List the windows of every monitor, not just the pointer's.</param>
    public void Open(int x, int y, bool allMonitors, Settings settings, Task<CaptureService> capture)
    {
        if (_open) Hide();

        nint monitor = User32.MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        var (bounds, work, dpi) = Monitors.Describe(monitor);
        _metrics = new Metrics(dpi / 96.0);
        Metrics m = _metrics;
        EnsureFont(m.FontHeight);
        UpdateAccent();

        _allMonitors = allMonitors;
        var groups = allMonitors ? WindowList.AllMonitors() : WindowList.ForMonitor(monitor);
        _order.Apply(groups, settings.RowOrder);
        _peek.Learn(groups.SelectMany(g => g.Windows));
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

        // The anchor thumbnail is the active window's, or, when the active window is not in the popup (it is
        // on another monitor, or it is the desktop), the foremost window's on this monitor. The popup opens
        // with that thumbnail centered under the pointer, moved only as far as the work area requires; if it
        // had to move, the pointer follows the thumbnail. Without an anchor, the popup opens a little right
        // of the pointer, centered on it vertically, and if it had to move the pointer goes to the same spot
        // beside it.
        var (active, kind) = AnchorTile(monitor);
        int anchorX = 0, anchorY = 0;
        int wantLeft, wantTop;
        if (active >= 0)
        {
            RECT box = _tiles[active].Layout.ImageBox;
            anchorX = box.Left + box.Width / 2;
            anchorY = box.Top + box.Height / 2;
            wantLeft = x - anchorX;
            wantTop = y - anchorY;
        }
        else
        {
            wantLeft = x + m.PointerOffset;
            wantTop = y - _height / 2;
        }
        _left = Math.Max(work.Left + m.ScreenMargin, Math.Min(wantLeft, work.Right - m.ScreenMargin - _width));
        _top = Math.Max(work.Top + m.ScreenMargin, Math.Min(wantTop, work.Bottom - m.ScreenMargin - _height));
        bool moved = _left != wantLeft || _top != wantTop;

        string anchor = "none";
        int pointerX = x, pointerY = y;
        if (active >= 0)
        {
            pointerX = _left + anchorX;
            pointerY = _top + anchorY;
            anchor = kind;
        }
        else if (moved)
        {
            pointerX = Math.Clamp(_left - m.PointerOffset, bounds.Left, bounds.Right - 1);
            pointerY = Math.Clamp(_top + _height / 2, bounds.Top, bounds.Bottom - 1);
            anchor = "beside";
        }
        if (pointerX != x || pointerY != y)
        {
            User32.SetCursorPos(pointerX, pointerY);
            InputHook.SetLastPoint(pointerX, pointerY);
        }

        CreateBuffer(_width, _height);
        _hover = -1;
        _open = true;
        RenderAll();
        User32.SetWindowPos(_hwnd, HWND_TOPMOST, _left, _top, _width, _height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Invalidate(new RECT(0, 0, _width, _height));
        ShowDimmer(settings.DimPercent);
        foreach (var tile in _tiles)
            if (tile.Thumbnail != 0) ShowThumbnail(tile);

        Hover(pointerX, pointerY);
        StartCaptures(capture);
        WriteGateLayout(bounds, work, dpi, pointerX, pointerY, moved, anchor, active >= 0 ? _tiles[active].Window.Hwnd : 0);
    }

    /// <summary>
    /// The tile the pointer starts on: the active window's ("active"), else the foremost non-minimized
    /// window on the given monitor ("foremost"); -1 if neither has a thumbnail inside the popup.
    /// </summary>
    (int Index, string Kind) AnchorTile(nint monitor)
    {
        int active = ActiveTile();
        if (active >= 0 && InsidePopup(active)) return (active, "active");

        int foremost = -1;
        for (int i = 0; i < _tiles.Count; i++)
        {
            WindowInfo w = _tiles[i].Window;
            if (w.Minimized || w.Monitor != monitor || !InsidePopup(i)) continue;
            if (foremost < 0 || w.ZOrder < _tiles[foremost].Window.ZOrder) foremost = i;
        }
        return foremost >= 0 ? (foremost, "foremost") : (-1, "");
    }

    /// <summary>The tile of the foreground window, or of the window it belongs to (a dialog's owner); -1 if none.</summary>
    int ActiveTile()
    {
        nint foreground = User32.GetForegroundWindow();
        if (foreground == 0) return -1;
        nint root = User32.GetAncestor(foreground, GA_ROOTOWNER);
        int found = _tiles.FindIndex(t => t.Window.Hwnd == foreground);
        if (found < 0) found = _tiles.FindIndex(t => t.Window.Hwnd == root);
        if (found < 0) found = _tiles.FindIndex(t => User32.GetAncestor(t.Window.Hwnd, GA_ROOTOWNER) == root);
        return found;
    }

    bool InsidePopup(int index)
    {
        RECT box = _tiles[index].Layout.ImageBox;
        return box.Right <= _width && box.Bottom <= _height;
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

        // The hovered window is shown as if in front; leaving its thumbnail puts things back.
        if (hit >= 0)
        {
            _peek.Show(_tiles[hit].Window, _hwnd);
            GateLine($"peek {_tiles[hit].Window.Hwnd:X}");
        }
        else
        {
            _peek.Hide();
            GateLine("peek 0");
        }
    }

    /// <summary>
    /// Hides the popup and returns the window under (x, y), or 0. The peek of that window and the dimming
    /// stay up (the caller hides them with <see cref="HideOverlays"/> once the real window is in front),
    /// so nothing flickers.
    /// </summary>
    public nint Close(int x, int y)
    {
        if (!_open) return 0;
        int hit = HitTest(x, y);
        nint target = hit >= 0 ? _tiles[hit].Window.Hwnd : 0;
        Hide(keepPeek: target != 0);
        GateLine($"closed {x} {y} hit {target:X}");
        return target;
    }

    /// <summary>Esc: closes the popup as if the button were released away from the thumbnails.</summary>
    public void Cancel()
    {
        if (!_open) return;
        Hide();
        GateLine("cancelled");
    }

    /// <summary>Hides the peek and the dimming.</summary>
    public void HideOverlays()
    {
        _peek.Hide();
        _dimmer?.Hide();
    }

    void ShowDimmer(int percent)
    {
        if (_dimmer is null) return;
        try
        {
            _dimmer.Show(_hwnd, percent / 100f);
        }
        catch (Exception e)
        {
            Log.Error("showing the dimming layer", e);
        }
    }

    public void Hide(bool keepPeek = false)
    {
        if (!keepPeek) HideOverlays();
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

    // GDI draws only text, icons and thumbnails, and only onto an opaque plate. GDI does not keep a 32-bit
    // buffer's alpha channel, so those areas are made opaque again afterwards; everything with a
    // transparent edge (plates, the hover border, placeholders) is drawn by the pixel code below.

    void RenderAll()
    {
        Gdi32.GdiFlush();
        Clear(new RECT(0, 0, _width, _height));
        if (_tiles.Count == 0)
        {
            var all = new RECT(0, 0, _width, _height);
            FillRoundRect(all, _metrics.Radius, PlateColor);
            var text = Inset(all, _metrics.Radius);
            DrawText(_allMonitors ? "No windows" : "No windows on this monitor", text,
                DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
            Gdi32.GdiFlush();
            MakeOpaque(text);
            return;
        }
        for (int i = 0; i < _tiles.Count; i++)
            RenderTile(i);
    }

    void RenderTile(int index)
    {
        Tile tile = _tiles[index];
        TileLayout l = tile.Layout;
        Metrics m = _metrics;
        bool hot = index == _hover;

        // The header and image box lie inside the plate's opaque interior (the inset exceeds what the
        // rounded corners cut away), so GDI can draw there.
        Gdi32.GdiFlush();
        Clear(l.Tile);
        FillRoundRect(l.Tile, m.Radius, hot ? HoverColor : PlateColor);
        StrokeRoundRect(l.Tile, m.Radius, m.Outline, OutlineColor);
        if (hot) StrokeRoundRect(l.Tile, m.Radius, m.Border, _accent);

        DrawTileContent(tile, l, m);

        Gdi32.GdiFlush();
        MakeOpaque(l.Header);
        MakeOpaque(l.ImageBox);
    }

    unsafe void DrawTileContent(Tile tile, TileLayout l, Metrics m)
    {
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
        Gdi32.GdiFlush();
        FillRoundRect(place, m.Radius / 2, PlaceholderColor);

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

    // ---- pixel drawing (premultiplied BGRA in _bits) ----

    static RECT Inset(RECT r, int by) => new(r.Left + by, r.Top + by, r.Right - by, r.Bottom - by);

    RECT ClipToBuffer(RECT r) => new(Math.Max(0, r.Left), Math.Max(0, r.Top), Math.Min(_width, r.Right), Math.Min(_height, r.Bottom));

    unsafe void Clear(RECT r)
    {
        r = ClipToBuffer(r);
        for (int y = r.Top; y < r.Bottom; y++)
            new Span<byte>((byte*)_bits + ((long)y * _width + r.Left) * 4, Math.Max(0, r.Width) * 4).Clear();
    }

    unsafe void MakeOpaque(RECT r)
    {
        r = ClipToBuffer(r);
        for (int y = r.Top; y < r.Bottom; y++)
        {
            byte* p = (byte*)_bits + ((long)y * _width + r.Left) * 4;
            for (int x = r.Left; x < r.Right; x++, p += 4)
                p[3] = 255;
        }
    }

    /// <summary>An anti-aliased rounded rectangle in an opaque color, blended over what is there.</summary>
    void FillRoundRect(RECT r, double radius, uint color)
    {
        radius = ClampRadius(r, radius);
        RECT clip = ClipToBuffer(r);
        for (int y = clip.Top; y < clip.Bottom; y++)
            for (int x = clip.Left; x < clip.Right; x++)
                BlendPixel(x, y, color, Coverage(x + 0.5, y + 0.5, r, radius));
    }

    /// <summary>An anti-aliased rounded outline of the given thickness, inside the rectangle.</summary>
    void StrokeRoundRect(RECT r, double radius, int thickness, uint color)
    {
        radius = ClampRadius(r, radius);
        RECT inner = Inset(r, thickness);
        double innerRadius = inner.Width > 0 && inner.Height > 0 ? ClampRadius(inner, radius - thickness) : 0;
        RECT clip = ClipToBuffer(r);
        for (int y = clip.Top; y < clip.Bottom; y++)
        {
            for (int x = clip.Left; x < clip.Right; x++)
            {
                double outer = Coverage(x + 0.5, y + 0.5, r, radius);
                double hole = innerRadius > 0 ? Coverage(x + 0.5, y + 0.5, inner, innerRadius) : 0;
                BlendPixel(x, y, color, outer - hole);
            }
        }
    }

    // Below half a pixel the distance formula stops being a coverage; above half the side it has no center.
    static double ClampRadius(RECT r, double radius) =>
        Math.Clamp(radius, 0.5, Math.Max(0.5, Math.Min(r.Width, r.Height) / 2.0));

    /// <summary>How much of the pixel centered at (px, py) a rounded rectangle covers, from its distance to the edge.</summary>
    static double Coverage(double px, double py, RECT r, double radius)
    {
        double cx = Math.Clamp(px, r.Left + radius, r.Right - radius);
        double cy = Math.Clamp(py, r.Top + radius, r.Bottom - radius);
        double dx = px - cx, dy = py - cy;
        return Math.Clamp(radius - Math.Sqrt(dx * dx + dy * dy) + 0.5, 0, 1);
    }

    /// <summary>Source-over of an opaque COLORREF with the given coverage, in premultiplied BGRA.</summary>
    unsafe void BlendPixel(int x, int y, uint color, double coverage)
    {
        int a = (int)(coverage * 255 + 0.5);
        if (a <= 0) return;
        byte* p = (byte*)_bits + ((long)y * _width + x) * 4;
        int inv = 255 - a;
        int r = (int)(color & 0xFF), g = (int)((color >> 8) & 0xFF), b = (int)((color >> 16) & 0xFF);
        p[0] = (byte)((b * a + p[0] * inv + 127) / 255);
        p[1] = (byte)((g * a + p[1] * inv + 127) / 255);
        p[2] = (byte)((r * a + p[2] * inv + 127) / 255);
        p[3] = (byte)(a + (p[3] * inv + 127) / 255);
    }

    void UpdateAccent() => _accent = ReadAccent();

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
        _buffer = Gdi32.CreateDIBSection(0, &bmi, DIB_RGB_COLORS, out byte* bits, 0, 0);
        _bits = (nint)bits;
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
            _bits = 0;
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

    void WriteGateLayout(RECT bounds, RECT work, uint dpi, int pointerX, int pointerY, bool moved, string anchor, nint active)
    {
        if (_gateLayoutPath is null) return;
        var sb = new StringBuilder();
        sb.AppendLine($"popup {_left} {_top} {_left + _width} {_top + _height}");
        sb.AppendLine($"monitor {bounds.Left} {bounds.Top} {bounds.Right} {bounds.Bottom}");
        sb.AppendLine($"work {work.Left} {work.Top} {work.Right} {work.Bottom}");
        sb.AppendLine($"dpi {dpi} offset {_metrics.PointerOffset}");
        sb.AppendLine($"pointer {pointerX} {pointerY} moved {(moved ? 1 : 0)} anchor {anchor} active {active:X}");
        sb.AppendLine($"scope {(_allMonitors ? "all" : "this")}");
        // tile HWND  tile rect  minimized  dwm-image  image-box rect  title
        foreach (var tile in _tiles)
        {
            RECT r = tile.Layout.Tile;
            RECT b = tile.Layout.ImageBox;
            sb.AppendLine($"tile {tile.Window.Hwnd:X} {r.Left + _left} {r.Top + _top} {r.Right + _left} {r.Bottom + _top} " +
                $"{(tile.Window.Minimized ? 1 : 0)} {(tile.Thumbnail != 0 ? 1 : 0)} " +
                $"{b.Left + _left} {b.Top + _top} {b.Right + _left} {b.Bottom + _top} {tile.Window.Title}");
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
