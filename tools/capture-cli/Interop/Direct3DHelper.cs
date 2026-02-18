using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace CaptureCli.Interop;

/// <summary>
/// Direct3D11 디바이스 생성 및 WinRT GraphicsCaptureItem 인터롭 헬퍼
/// </summary>
internal static class Direct3DHelper
{
    // ─── IGraphicsCaptureItemInterop COM Interface ────────

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(
            IntPtr window,
            ref Guid iid,
            out IntPtr result);

        [PreserveSig]
        int CreateForMonitor(
            IntPtr monitor,
            ref Guid iid,
            out IntPtr result);
    }

    // IGraphicsCaptureItem WinRT interface IID
    private static readonly Guid IID_IGraphicsCaptureItem =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    /// <summary>
    /// D3D11 디바이스를 생성하고 WinRT IDirect3DDevice로 래핑하여 반환
    /// </summary>
    public static IDirect3DDevice CreateDevice()
    {
        var hr = NativeMethods.D3D11CreateDevice(
            IntPtr.Zero,
            NativeMethods.D3D_DRIVER_TYPE_HARDWARE,
            IntPtr.Zero,
            NativeMethods.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            IntPtr.Zero,
            0,
            NativeMethods.D3D11_SDK_VERSION,
            out var devicePtr,
            out _,
            out var contextPtr);

        if (hr != 0)
            throw new COMException("D3D11CreateDevice failed", hr);

        try
        {
            // ID3D11Device → IDXGIDevice
            var dxgiIid = NativeMethods.IID_IDXGIDevice;
            Marshal.QueryInterface(devicePtr, ref dxgiIid, out var dxgiDevicePtr);

            try
            {
                // IDXGIDevice → WinRT IDirect3DDevice
                NativeMethods.CreateDirect3D11DeviceFromDXGIDevice(
                    dxgiDevicePtr, out var winrtDevicePtr);

                var d3dDevice = MarshalInterface<IDirect3DDevice>.FromAbi(winrtDevicePtr);
                Marshal.Release(winrtDevicePtr);
                return d3dDevice;
            }
            finally
            {
                Marshal.Release(dxgiDevicePtr);
            }
        }
        finally
        {
            Marshal.Release(contextPtr);
            Marshal.Release(devicePtr);
        }
    }

    /// <summary>
    /// HWND로부터 GraphicsCaptureItem 생성
    /// </summary>
    public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        var interop = GetCaptureInterop();
        var itemGuid = IID_IGraphicsCaptureItem;
        var hr = interop.CreateForWindow(hwnd, ref itemGuid, out var ptr);
        Marshal.ThrowExceptionForHR(hr);

        var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(ptr);
        Marshal.Release(ptr);
        return item;
    }

    /// <summary>
    /// HMONITOR로부터 GraphicsCaptureItem 생성
    /// </summary>
    public static GraphicsCaptureItem CreateItemForMonitor(IntPtr hMonitor)
    {
        var interop = GetCaptureInterop();
        var itemGuid = IID_IGraphicsCaptureItem;
        var hr = interop.CreateForMonitor(hMonitor, ref itemGuid, out var ptr);
        Marshal.ThrowExceptionForHR(hr);

        var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(ptr);
        Marshal.Release(ptr);
        return item;
    }

    /// <summary>
    /// GraphicsCaptureItem 활성화 팩토리에서 IGraphicsCaptureItemInterop 획득
    /// </summary>
    private static IGraphicsCaptureItemInterop GetCaptureInterop()
    {
        var className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        NativeMethods.WindowsCreateString(className, className.Length, out var hstring);

        try
        {
            var interopGuid = typeof(IGraphicsCaptureItemInterop).GUID;
            var hr = NativeMethods.RoGetActivationFactory(
                hstring, ref interopGuid, out var factoryPtr);
            Marshal.ThrowExceptionForHR(hr);

            var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
            Marshal.Release(factoryPtr);
            return interop;
        }
        finally
        {
            NativeMethods.WindowsDeleteString(hstring);
        }
    }
}
