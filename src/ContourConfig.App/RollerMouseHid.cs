using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace ContourConfig.App
{
    // Reads input reports from the RollerMouse consumer-control HID collection, which carries the roller end markers.
    internal sealed class RollerMouseHid : IDisposable
    {
        private const ushort ConsumerPage = 0x000C;
        private const ushort ConsumerControl = 0x0001;
        private readonly FileStream stream;
        private readonly int reportLength;

        private RollerMouseHid(string path, SafeFileHandle handle, int reportLength)
        {
            Path = path;
            this.reportLength = reportLength;
            stream = new(handle, FileAccess.Read, 1, isAsync: true);
        }

        internal string Path { get; }

        internal static RollerMouseHid? Open()
        {
            foreach (string path in GetHidPaths().Where(IsContour))
            {
                if (TryGetCaps(path, out NativeMethods.HidCaps caps) && caps.UsagePage == ConsumerPage &&
                    caps.Usage == ConsumerControl && caps.InputReportByteLength > 7)
                {
                    return OpenReader(path, caps.InputReportByteLength);
                }
            }
            return null;
        }

        private static RollerMouseHid OpenReader(string path, int reportLength)
        {
            SafeFileHandle? handle = null;
            try
            {
                handle = NativeMethods.CreateFile(path, NativeMethods.GenericRead, NativeMethods.FileShareReadWrite, 0,
                    NativeMethods.OpenExisting, NativeMethods.FileFlagOverlapped, 0);
                if (handle.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Found the RollerMouse but could not open its end-marker channel.");
                }
                RollerMouseHid reader = new(path, handle, reportLength);
                handle = null;
                return reader;
            }
            finally
            {
                handle?.Dispose();
            }
        }

        // Completes when the device disconnects or cancellation is requested.
        internal async Task ReadAsync(Action<byte[]> onReport, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[reportLength];
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    return;
                }
                onReport(buffer[..read]);
            }
        }

        public void Dispose() => stream.Dispose();

        private static bool IsContour(string path) =>
            path.Contains("vid&020b33", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("vid_0b33", StringComparison.OrdinalIgnoreCase);

        private static bool TryGetCaps(string path, out NativeMethods.HidCaps caps)
        {
            caps = default;
            using SafeFileHandle handle = NativeMethods.CreateFile(path, 0, NativeMethods.FileShareReadWrite, 0,
                NativeMethods.OpenExisting, 0, 0);
            if (handle.IsInvalid || !NativeMethods.HidD_GetPreparsedData(handle, out nint preparsed))
            {
                return false;
            }
            try
            {
                return NativeMethods.HidP_GetCaps(preparsed, out caps) == NativeMethods.HidpStatusSuccess;
            }
            finally
            {
                NativeMethods.HidD_FreePreparsedData(preparsed);
            }
        }

        private static string[] GetHidPaths()
        {
            const int success = 0;
            const int bufferSmall = 0x1A;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int result = NativeMethods.GetDeviceInterfaceListSize(out uint length, NativeMethods.HidInterfaceClass, null, 0);
                if (result != success)
                {
                    throw new Win32Exception($"Could not list HID devices (CONFIGRET {result}).");
                }
                char[] buffer = new char[length];
                result = NativeMethods.GetDeviceInterfaceList(NativeMethods.HidInterfaceClass, null, buffer, length, 0);
                if (result == bufferSmall)
                {
                    continue;
                }
                if (result != success)
                {
                    throw new Win32Exception($"Could not list HID devices (CONFIGRET {result}).");
                }
                return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            }
            throw new Win32Exception("The HID device list kept changing. Try again.");
        }
    }
}
