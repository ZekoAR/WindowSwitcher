using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WindowSwitcher.Native;

namespace WindowSwitcher;

/// <summary>
/// Takes one Windows Graphics Capture frame of a window and turns it into a thumbnail. The frame is
/// shrunk on the GPU (mipmaps) so only thumbnail-sized pixels are read back.
/// </summary>
internal sealed class CaptureService : IDisposable
{
    readonly nint _device;
    readonly nint _context;
    readonly IDirect3DDevice _winrtDevice;
    readonly Lock _gpu = new(); // the D3D11 immediate context is single-threaded

    public CaptureService()
    {
        Direct3D.CreateDevice(out _device, out _context);
        _winrtDevice = Direct3D.CreateWinRTDevice(_device);
    }

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    /// <summary>The service plus the one-time borderless request; call it on a thread-pool thread.</summary>
    public static async Task<CaptureService> CreateAsync()
    {
        var service = new CaptureService();
        await RequestBorderlessAsync().ConfigureAwait(false);
        return service;
    }

    /// <summary>Asks once for capture without the yellow border (Windows 11). Failure only means the border shows.</summary>
    public static async Task RequestBorderlessAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)) return;
        try
        {
            await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
        }
        catch
        {
        }
    }

    public async Task<CapturedImage> CaptureAsync(nint hwnd, int boxWidth, int boxHeight, int timeoutMs, CancellationToken cancel = default)
    {
        GraphicsCaptureItem item = Direct3D.CreateCaptureItemForWindow(hwnd);
        SizeInt32 size = item.Size;
        if (size.Width <= 0 || size.Height <= 0)
            throw new InvalidOperationException($"capture item has no size ({size.Width}x{size.Height})");

        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
        {
            try
            {
                session.IsBorderRequired = false;
            }
            catch
            {
            }
        }

        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.FrameArrived += (_, _) => arrived.TrySetResult();
        session.StartCapture();

        Task finished = await Task.WhenAny(arrived.Task, Task.Delay(timeoutMs, cancel));
        cancel.ThrowIfCancellationRequested();
        if (finished != arrived.Task)
            throw new TimeoutException($"no frame within {timeoutMs} ms");

        using var frame = pool.TryGetNextFrame()
            ?? throw new InvalidOperationException("frame arrived but TryGetNextFrame returned null");
        return Read(frame.Surface, frame.ContentSize, boxWidth, boxHeight);
    }

    CapturedImage Read(IDirect3DSurface surface, SizeInt32 contentSize, int boxWidth, int boxHeight)
    {
        nint frameTexture = Direct3D.GetTextureFromSurface(surface);
        try
        {
            D3D11_TEXTURE2D_DESC desc = Direct3D.GetDesc(frameTexture);
            int w = Math.Min((int)desc.Width, contentSize.Width);
            int h = Math.Min((int)desc.Height, contentSize.Height);
            if (w <= 0 || h <= 0)
                throw new InvalidOperationException($"empty frame ({contentSize.Width}x{contentSize.Height})");

            var (tw, th) = Thumbnail.Fit(w, h, boxWidth, boxHeight);

            // The smallest mip level that is still at least the thumbnail size on both axes.
            int level = 0;
            while ((w >> (level + 1)) >= tw && (h >> (level + 1)) >= th)
                level++;
            int mw = Math.Max(1, w >> level);
            int mh = Math.Max(1, h >> level);

            byte[] pixels;
            lock (_gpu)
                pixels = level == 0 ? ReadRegion(frameTexture, w, h) : ReadMipLevel(frameTexture, w, h, level, mw, mh);

            return new CapturedImage(tw, th, Thumbnail.Resample(pixels, mw, mh, tw, th));
        }
        finally
        {
            Direct3D.Release(frameTexture);
        }
    }

    unsafe byte[] ReadRegion(nint source, int w, int h)
    {
        nint staging = CreateStaging(w, h);
        try
        {
            var box = new D3D11_BOX { right = (uint)w, bottom = (uint)h, back = 1 };
            Direct3D.CopySubresourceRegion(_context, staging, 0, source, 0, &box);
            return CopyOut(staging, w, h);
        }
        finally
        {
            Direct3D.Release(staging);
        }
    }

    unsafe byte[] ReadMipLevel(nint source, int w, int h, int level, int mw, int mh)
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = (uint)(level + 1),
            ArraySize = 1,
            Format = Direct3D.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleCount = 1,
            Usage = Direct3D.D3D11_USAGE_DEFAULT,
            BindFlags = Direct3D.D3D11_BIND_SHADER_RESOURCE | Direct3D.D3D11_BIND_RENDER_TARGET,
            MiscFlags = Direct3D.D3D11_RESOURCE_MISC_GENERATE_MIPS,
        };
        nint mipmapped = Direct3D.CreateTexture2D(_device, desc);
        nint view = 0, staging = 0;
        try
        {
            var box = new D3D11_BOX { right = (uint)w, bottom = (uint)h, back = 1 };
            Direct3D.CopySubresourceRegion(_context, mipmapped, 0, source, 0, &box);
            view = Direct3D.CreateShaderResourceView(_device, mipmapped);
            Direct3D.GenerateMips(_context, view);

            staging = CreateStaging(mw, mh);
            Direct3D.CopySubresourceRegion(_context, staging, 0, mipmapped, (uint)level, null);
            return CopyOut(staging, mw, mh);
        }
        finally
        {
            Direct3D.Release(staging);
            Direct3D.Release(view);
            Direct3D.Release(mipmapped);
        }
    }

    nint CreateStaging(int w, int h)
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = 1,
            ArraySize = 1,
            Format = Direct3D.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleCount = 1,
            Usage = Direct3D.D3D11_USAGE_STAGING,
            CPUAccessFlags = Direct3D.D3D11_CPU_ACCESS_READ,
        };
        return Direct3D.CreateTexture2D(_device, desc);
    }

    unsafe byte[] CopyOut(nint staging, int w, int h)
    {
        D3D11_MAPPED_SUBRESOURCE mapped = Direct3D.Map(_context, staging, 0, Direct3D.D3D11_MAP_READ);
        try
        {
            var result = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                new ReadOnlySpan<byte>(mapped.pData + (long)y * mapped.RowPitch, w * 4).CopyTo(result.AsSpan(y * w * 4));
            return result;
        }
        finally
        {
            Direct3D.Unmap(_context, staging, 0);
        }
    }

    public void Dispose()
    {
        _winrtDevice.Dispose();
        Direct3D.Release(_context);
        Direct3D.Release(_device);
    }
}
