using System.Runtime.InteropServices;
using CaptureCli.Interop;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace CaptureCli.Services;

/// <summary>
/// Windows Graphics Capture API를 사용한 화면 캡처 서비스
/// </summary>
internal class CaptureService : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// 윈도우 HWND를 캡처하여 이미지 파일로 저장
    /// </summary>
    public async Task<int> CaptureWindowAsync(
        IntPtr hwnd, string outputPath, string format, int delay, bool cropClient)
    {
        // 최소화 상태 확인
        if (NativeMethods.IsIconic(hwnd))
        {
            Console.Error.WriteLine("Error: Window is minimized. Cannot capture.");
            return 2;
        }

        if (delay > 0)
        {
            Console.WriteLine($"Waiting {delay} seconds...");
            await Task.Delay(delay * 1000);
        }

        try
        {
            // D3D11 디바이스 생성
            var d3dDevice = Direct3DHelper.CreateDevice();

            // HWND로부터 GraphicsCaptureItem 생성
            var item = Direct3DHelper.CreateItemForWindow(hwnd);

            // 캡처 실행
            var result = await CaptureItemAsync(
                d3dDevice, item, hwnd, outputPath, format, cropClient);

            return result;
        }
        catch (COMException ex)
        {
            Console.Error.WriteLine($"Capture failed (0x{ex.HResult:X8}): {ex.Message}");
            return 2;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Error: Insufficient permissions. Try running as administrator.");
            return 3;
        }
    }

    /// <summary>
    /// 모니터를 캡처하여 이미지 파일로 저장
    /// </summary>
    public async Task<int> CaptureMonitorAsync(
        IntPtr hMonitor, string outputPath, string format, int delay)
    {
        if (delay > 0)
        {
            Console.WriteLine($"Waiting {delay} seconds...");
            await Task.Delay(delay * 1000);
        }

        try
        {
            var d3dDevice = Direct3DHelper.CreateDevice();
            var item = Direct3DHelper.CreateItemForMonitor(hMonitor);

            var result = await CaptureItemAsync(
                d3dDevice, item, IntPtr.Zero, outputPath, format, false);

            return result;
        }
        catch (COMException ex)
        {
            Console.Error.WriteLine($"Capture failed (0x{ex.HResult:X8}): {ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// GraphicsCaptureItem을 캡처하여 이미지 저장
    /// </summary>
    private async Task<int> CaptureItemAsync(
        Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice d3dDevice,
        GraphicsCaptureItem item,
        IntPtr hwnd,
        string outputPath,
        string format,
        bool cropClient)
    {
        // 프레임 풀 생성 (FreeThreaded: 콘솔앱에서 메시지 펌프 불필요)
        var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            d3dDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            item.Size);

        // 프레임 도착 대기
        var tcs = new TaskCompletionSource<Direct3D11CaptureFrame>();
        pool.FrameArrived += (sender, args) =>
        {
            var frame = sender.TryGetNextFrame();
            if (frame != null)
                tcs.TrySetResult(frame);
        };

        // 캡처 세션 시작
        var session = pool.CreateCaptureSession(item);

        // 캡처 테두리 숨기기 (Windows 11+에서만 지원)
        TryDisableBorder(session);

        session.StartCapture();

        // 타임아웃 설정 (5초)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cts.Token.Register(() => tcs.TrySetCanceled());

        Direct3D11CaptureFrame capturedFrame;
        try
        {
            capturedFrame = await tcs.Task;
        }
        catch (TaskCanceledException)
        {
            session.Dispose();
            pool.Dispose();
            Console.Error.WriteLine("Error: Capture timed out.");
            return 2;
        }

        try
        {
            // 이미지 저장
            await SaveFrameAsync(capturedFrame, hwnd, outputPath, format, cropClient);
            Console.WriteLine($"Captured: {Path.GetFullPath(outputPath)}");
            return 0;
        }
        finally
        {
            capturedFrame.Dispose();
            session.Dispose();
            pool.Dispose();
        }
    }

    /// <summary>
    /// 캡처한 프레임을 이미지 파일로 저장
    /// </summary>
    private static async Task SaveFrameAsync(
        Direct3D11CaptureFrame frame,
        IntPtr hwnd,
        string outputPath,
        string format,
        bool cropClient)
    {
        // IDirect3DSurface → SoftwareBitmap 변환
        var softwareBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
            frame.Surface, BitmapAlphaMode.Premultiplied);

        // 인코더 ID 결정
        var encoderId = format.ToLower() switch
        {
            "jpg" or "jpeg" => BitmapEncoder.JpegEncoderId,
            _ => BitmapEncoder.PngEncoderId
        };

        // 메모리 스트림에 인코딩
        using var memStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(encoderId, memStream);
        encoder.SetSoftwareBitmap(softwareBitmap);

        // 클라이언트 영역 크롭
        if (cropClient && hwnd != IntPtr.Zero)
        {
            var cropBounds = GetClientAreaCropBounds(hwnd, frame.ContentSize);
            if (cropBounds.HasValue)
            {
                encoder.BitmapTransform.Bounds = cropBounds.Value;
            }
        }

        // JPEG 품질 설정
        if (format.ToLower() is "jpg" or "jpeg")
        {
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
            var props = new BitmapPropertySet
            {
                { "ImageQuality", new BitmapTypedValue(0.9, Windows.Foundation.PropertyType.Single) }
            };
        }

        await encoder.FlushAsync();

        // 파일로 저장
        var bytes = new byte[memStream.Size];
        memStream.Seek(0);
        using var reader = new DataReader(memStream);
        await reader.LoadAsync((uint)memStream.Size);
        reader.ReadBytes(bytes);

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(outputPath, bytes);
    }

    /// <summary>
    /// 클라이언트 영역의 크롭 바운드를 계산
    /// </summary>
    private static BitmapBounds? GetClientAreaCropBounds(IntPtr hwnd, SizeInt32 captureSize)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect))
            return null;

        if (!NativeMethods.GetClientRect(hwnd, out var clientRect))
            return null;

        var clientOrigin = new NativeMethods.POINT { X = 0, Y = 0 };
        NativeMethods.ClientToScreen(hwnd, ref clientOrigin);

        // DWM 확장 프레임 바운드 사용 (DPI 정확도 향상)
        var dwmResult = NativeMethods.DwmGetWindowAttribute(
            hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out var extendedBounds,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>());

        var refRect = dwmResult == 0 ? extendedBounds : windowRect;

        var cropLeft = (uint)(clientOrigin.X - refRect.Left);
        var cropTop = (uint)(clientOrigin.Y - refRect.Top);
        var cropWidth = (uint)clientRect.Width;
        var cropHeight = (uint)clientRect.Height;

        // 캡처 크기 범위 내로 제한
        if (cropLeft + cropWidth > (uint)captureSize.Width)
            cropWidth = (uint)captureSize.Width - cropLeft;
        if (cropTop + cropHeight > (uint)captureSize.Height)
            cropHeight = (uint)captureSize.Height - cropTop;

        if (cropWidth == 0 || cropHeight == 0)
            return null;

        return new BitmapBounds
        {
            X = cropLeft,
            Y = cropTop,
            Width = cropWidth,
            Height = cropHeight
        };
    }

    /// <summary>
    /// 캡처 세션의 테두리를 비활성화 (지원 시)
    /// </summary>
    private static void TryDisableBorder(GraphicsCaptureSession session)
    {
        try
        {
            // IsBorderRequired는 Windows 11(10.0.22000)부터 지원
            var prop = typeof(GraphicsCaptureSession).GetProperty("IsBorderRequired");
            prop?.SetValue(session, false);
        }
        catch
        {
            // 지원하지 않는 OS에서는 무시
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
