using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace WindowSwitcher.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_TEXTURE2D_DESC
{
    public uint Width;
    public uint Height;
    public uint MipLevels;
    public uint ArraySize;
    public uint Format;
    public uint SampleCount;
    public uint SampleQuality;
    public uint Usage;
    public uint BindFlags;
    public uint CPUAccessFlags;
    public uint MiscFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct D3D11_MAPPED_SUBRESOURCE
{
    public byte* pData;
    public uint RowPitch;
    public uint DepthPitch;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_BOX
{
    public uint left, top, front, right, bottom, back;
}

/// <summary>
/// Raw COM calls for the few D3D11 / DXGI / WinRT-interop methods the capture path needs. Native AOT
/// has no built-in COM interop, so these go through vtable function pointers. Slot numbers were
/// counted from the Windows SDK 10.0.26100 headers (d3d11.h, Windows.Graphics.Capture.Interop.h,
/// windows.graphics.directx.direct3d11.interop.h).
/// </summary>
internal static unsafe partial class Direct3D
{
    public const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    public const uint D3D11_USAGE_DEFAULT = 0;
    public const uint D3D11_USAGE_STAGING = 3;
    public const uint D3D11_BIND_SHADER_RESOURCE = 0x8;
    public const uint D3D11_BIND_RENDER_TARGET = 0x20;
    public const uint D3D11_CPU_ACCESS_READ = 0x20000;
    public const uint D3D11_RESOURCE_MISC_GENERATE_MIPS = 0x1;
    public const uint D3D11_MAP_READ = 1;
    const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    const int D3D_DRIVER_TYPE_HARDWARE = 1;
    const int D3D_DRIVER_TYPE_WARP = 5;
    const uint D3D11_SDK_VERSION = 7;

    static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags,
        nint featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint activatableClassId, Guid* iid, out nint factory);

    [LibraryImport("combase.dll")]
    private static partial int WindowsCreateString(char* source, uint length, out nint hstring);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint hstring);

    static nint s_captureItemInterop;

    static nint* Vtbl(nint obj) => *(nint**)obj;

    public static void Release(nint obj)
    {
        if (obj != 0) Marshal.Release(obj);
    }

    /// <summary>Creates a hardware D3D11 device (WARP if no hardware device is available).</summary>
    public static void CreateDevice(out nint device, out nint context)
    {
        int hr = D3D11CreateDevice(0, D3D_DRIVER_TYPE_HARDWARE, 0, D3D11_CREATE_DEVICE_BGRA_SUPPORT, 0, 0,
            D3D11_SDK_VERSION, out device, out _, out context);
        if (hr < 0)
            hr = D3D11CreateDevice(0, D3D_DRIVER_TYPE_WARP, 0, D3D11_CREATE_DEVICE_BGRA_SUPPORT, 0, 0,
                D3D11_SDK_VERSION, out device, out _, out context);
        Marshal.ThrowExceptionForHR(hr);
    }

    /// <summary>Wraps a D3D11 device as the WinRT IDirect3DDevice the capture frame pool takes.</summary>
    public static IDirect3DDevice CreateWinRTDevice(nint d3dDevice)
    {
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, in IID_IDXGIDevice, out nint dxgi));
        try
        {
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out nint inspectable));
            try
            {
                return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            }
            finally
            {
                Marshal.Release(inspectable);
            }
        }
        finally
        {
            Marshal.Release(dxgi);
        }
    }

    /// <summary>IGraphicsCaptureItemInterop::CreateForWindow (slot 3).</summary>
    public static GraphicsCaptureItem CreateCaptureItemForWindow(nint hwnd)
    {
        nint factory = GetCaptureItemInterop();
        Guid iid = IID_IGraphicsCaptureItem;
        nint item;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Vtbl(factory)[3])(factory, hwnd, &iid, &item);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return GraphicsCaptureItem.FromAbi(item);
        }
        finally
        {
            Marshal.Release(item);
        }
    }

    static nint GetCaptureItemInterop()
    {
        if (s_captureItemInterop != 0) return s_captureItemInterop;
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        nint hstring;
        fixed (char* p = className)
            Marshal.ThrowExceptionForHR(WindowsCreateString(p, (uint)className.Length, out hstring));
        try
        {
            Guid iid = IID_IGraphicsCaptureItemInterop;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, &iid, out nint factory));
            nint previous = Interlocked.CompareExchange(ref s_captureItemInterop, factory, 0);
            if (previous != 0)
            {
                Marshal.Release(factory);
                return previous;
            }
            return factory;
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    /// <summary>
    /// The ID3D11Texture2D behind a WinRT IDirect3DSurface, through
    /// IDirect3DDxgiInterfaceAccess::GetInterface (slot 3). The caller releases the result.
    /// </summary>
    public static nint GetTextureFromSurface(IDirect3DSurface surface)
    {
        nint surfacePtr = ((IWinRTObject)surface).NativeObject.ThisPtr;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surfacePtr, in IID_IDirect3DDxgiInterfaceAccess, out nint access));
        GC.KeepAlive(surface);
        try
        {
            Guid iid = IID_ID3D11Texture2D;
            nint texture;
            int hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Vtbl(access)[3])(access, &iid, &texture);
            Marshal.ThrowExceptionForHR(hr);
            return texture;
        }
        finally
        {
            Marshal.Release(access);
        }
    }

    // ID3D11Texture2D::GetDesc (slot 10)
    public static D3D11_TEXTURE2D_DESC GetDesc(nint texture)
    {
        D3D11_TEXTURE2D_DESC desc;
        ((delegate* unmanaged[Stdcall]<nint, D3D11_TEXTURE2D_DESC*, void>)Vtbl(texture)[10])(texture, &desc);
        return desc;
    }

    // ID3D11Device::CreateTexture2D (slot 5)
    public static nint CreateTexture2D(nint device, in D3D11_TEXTURE2D_DESC desc)
    {
        nint texture;
        fixed (D3D11_TEXTURE2D_DESC* d = &desc)
        {
            int hr = ((delegate* unmanaged[Stdcall]<nint, D3D11_TEXTURE2D_DESC*, void*, nint*, int>)Vtbl(device)[5])(device, d, null, &texture);
            Marshal.ThrowExceptionForHR(hr);
        }
        return texture;
    }

    // ID3D11Device::CreateShaderResourceView (slot 7), default description
    public static nint CreateShaderResourceView(nint device, nint resource)
    {
        nint view;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint, void*, nint*, int>)Vtbl(device)[7])(device, resource, null, &view);
        Marshal.ThrowExceptionForHR(hr);
        return view;
    }

    // ID3D11DeviceContext::CopySubresourceRegion (slot 46)
    public static void CopySubresourceRegion(nint context, nint dst, uint dstSubresource, nint src, uint srcSubresource, D3D11_BOX* box)
    {
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, uint, nint, uint, D3D11_BOX*, void>)Vtbl(context)[46])(
            context, dst, dstSubresource, 0, 0, 0, src, srcSubresource, box);
    }

    // ID3D11DeviceContext::GenerateMips (slot 54)
    public static void GenerateMips(nint context, nint shaderResourceView)
    {
        ((delegate* unmanaged[Stdcall]<nint, nint, void>)Vtbl(context)[54])(context, shaderResourceView);
    }

    // ID3D11DeviceContext::Map (slot 14)
    public static D3D11_MAPPED_SUBRESOURCE Map(nint context, nint resource, uint subresource, uint mapType)
    {
        D3D11_MAPPED_SUBRESOURCE mapped;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, D3D11_MAPPED_SUBRESOURCE*, int>)Vtbl(context)[14])(
            context, resource, subresource, mapType, 0, &mapped);
        Marshal.ThrowExceptionForHR(hr);
        return mapped;
    }

    // ID3D11DeviceContext::Unmap (slot 15)
    public static void Unmap(nint context, nint resource, uint subresource)
    {
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)Vtbl(context)[15])(context, resource, subresource);
    }
}
