using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WindowSwitcher.Native;
using WinRT;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>
/// Dims every monitor while the popup is open (by the percentage in the settings): one window per
/// monitor, just below the popup and the peek, holding a Windows.UI.Composition sprite painted black at
/// that opacity, faded in and out by a composition animation (which DWM runs, not this thread). The
/// windows have no redirection bitmap (WS_EX_NOREDIRECTIONBITMAP), so the layer costs a color, not a
/// screen-sized image per monitor, and they are layered and transparent so clicks pass through, also
/// while the layer fades out after a switch. Composition needs a DispatcherQueue on the thread that uses
/// it, which is created on the UI thread; everything here runs on that thread.
/// </summary>
internal sealed unsafe partial class Dimmer
{
    const string ClassName = "WindowSwitcher.Dim";
    const int FadeMs = 120;

    [StructLayout(LayoutKind.Sequential)]
    struct DispatcherQueueOptions
    {
        public uint dwSize;
        public int threadType;     // DQTYPE_THREAD_CURRENT = 2
        public int apartmentType;  // DQTAT_COM_NONE = 0: this thread's COM apartment already exists
    }

    [LibraryImport("CoreMessaging.dll")]
    private static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    static readonly Guid IID_ICompositorDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");

    readonly nint _instance;
    readonly nint _queueController;
    readonly Compositor _compositor;
    readonly List<Layer> _layers = [];
    int _generation; // a newer Show or Hide cancels a pending hide at the end of a fade-out

    sealed class Layer(nint hwnd, DesktopWindowTarget target, SpriteVisual visual)
    {
        public nint Hwnd { get; } = hwnd;
        public DesktopWindowTarget Target { get; } = target;
        public SpriteVisual Visual { get; } = visual;
    }

    public Dimmer(nint instance)
    {
        _instance = instance;
        var options = new DispatcherQueueOptions { dwSize = (uint)sizeof(DispatcherQueueOptions), threadType = 2, apartmentType = 0 };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _queueController));
        _compositor = new Compositor();

        fixed (char* cls = ClassName)
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
                throw new InvalidOperationException($"RegisterClassEx(dim) failed: {Marshal.GetLastPInvokeError()}");
        }
    }

    /// <summary>
    /// Covers every monitor, directly below <paramref name="insertAfter"/> (the popup), darkening it by
    /// <paramref name="opacity"/> (0 to 1); 0 shows nothing.
    /// </summary>
    public void Show(nint insertAfter, float opacity)
    {
        _generation++;
        if (opacity <= 0)
        {
            HideNow();
            return;
        }
        List<RECT> monitors = MonitorBounds();
        while (_layers.Count < monitors.Count)
            _layers.Add(CreateLayer());
        for (int i = 0; i < _layers.Count; i++)
        {
            if (i >= monitors.Count)
            {
                HideNow(_layers[i]);
                continue;
            }
            RECT r = monitors[i];
            User32.SetWindowPos(_layers[i].Hwnd, insertAfter, r.Left, r.Top, r.Width, r.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            FadeTo(_layers[i].Visual, opacity); // from wherever it is: 0 when hidden, or mid fade-out
        }
    }

    /// <summary>Fades the layer out, then hides its windows.</summary>
    public void Hide()
    {
        int generation = ++_generation;
        var shown = _layers.Where(l => User32.IsWindowVisible(l.Hwnd)).ToList();
        if (shown.Count == 0) return;
        CompositionScopedBatch batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        foreach (var layer in shown)
            FadeTo(layer.Visual, 0);
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (generation != _generation) return; // shown again meanwhile
            foreach (var layer in shown) HideNow(layer);
        };
    }

    void HideNow()
    {
        foreach (var layer in _layers) HideNow(layer);
    }

    static void HideNow(Layer layer)
    {
        User32.ShowWindow(layer.Hwnd, SW_HIDE);
        layer.Visual.StopAnimation("Opacity");
        layer.Visual.Opacity = 0;
    }

    void FadeTo(SpriteVisual visual, float opacity)
    {
        ScalarKeyFrameAnimation fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, opacity, _compositor.CreateLinearEasingFunction());
        fade.Duration = TimeSpan.FromMilliseconds(FadeMs);
        visual.StartAnimation("Opacity", fade);
    }

    Layer CreateLayer()
    {
        nint hwnd;
        fixed (char* cls = ClassName)
            hwnd = User32.CreateWindowExW(
                WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT,
                cls, cls, WS_POPUP, 0, 0, 1, 1, 0, 0, _instance, 0);
        if (hwnd == 0)
            throw new InvalidOperationException($"CreateWindowEx(dim) failed: {Marshal.GetLastPInvokeError()}");
        User32.SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);

        DesktopWindowTarget target = CreateTarget(hwnd);
        SpriteVisual visual = _compositor.CreateSpriteVisual();
        visual.Brush = _compositor.CreateColorBrush(Color.FromArgb(255, 0, 0, 0));
        visual.RelativeSizeAdjustment = Vector2.One;
        visual.Opacity = 0; // every Show fades in from here
        target.Root = visual;
        return new Layer(hwnd, target, visual);
    }

    /// <summary>ICompositorDesktopInterop::CreateDesktopWindowTarget (slot 3), not topmost within the window.</summary>
    DesktopWindowTarget CreateTarget(nint hwnd)
    {
        nint compositor = ((IWinRTObject)_compositor).NativeObject.ThisPtr;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(compositor, in IID_ICompositorDesktopInterop, out nint interop));
        GC.KeepAlive(_compositor);
        try
        {
            nint target;
            int hr = ((delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)(*(nint**)interop)[3])(interop, hwnd, 0, &target);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                return DesktopWindowTarget.FromAbi(target);
            }
            finally
            {
                Marshal.Release(target);
            }
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    static List<RECT> MonitorBounds()
    {
        var list = new List<RECT>();
        var handle = GCHandle.Alloc(list);
        try
        {
            User32.EnumDisplayMonitors(0, null, &CollectMonitor, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return list;
    }

    [UnmanagedCallersOnly]
    static int CollectMonitor(nint monitor, nint hdc, RECT* clip, nint param)
    {
        ((List<RECT>)GCHandle.FromIntPtr(param).Target!).Add(Monitors.Describe(monitor).Bounds);
        return 1;
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                return 1; // composition draws the content
            case WM_PAINT:
                PAINTSTRUCT ps;
                User32.BeginPaint(hwnd, &ps);
                User32.EndPaint(hwnd, &ps);
                return 0;
            case WM_MOUSEACTIVATE:
                return MA_NOACTIVATE;
            case WM_DPICHANGED:
                return 0; // placed in physical pixels already
        }
        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
