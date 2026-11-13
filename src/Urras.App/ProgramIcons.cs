using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Urras.App;

// Editors' and shells' own icons, read from the programs installed on this computer (Windows only)
public static class ProgramIcons
{
    private const int BYTES_PER_PIXEL = 4;
    private const int ALPHA_OFFSET = 3;
    private const byte OPAQUE = 255;
    private const uint DIB_RGB_COLORS = 0;
    private const double SCREEN_DPI = 96;

    private static readonly Dictionary<string, Bitmap?> Cache = new Dictionary<string, Bitmap?>();

    // The icon for an editor or shell Urras knows, or null when it isn't installed or the icon can't be read
    public static Bitmap? For(string name)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        lock (Cache)
        {
            if (!Cache.TryGetValue(name, out Bitmap? icon))
            {
                icon = Locate(name) is { } program ? Extract(program) : null;
                Cache[name] = icon;
            }

            return icon;
        }
    }

    // Where each known program is usually installed; the first that exists is used
    private static string? Locate(string name)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        IEnumerable<string> candidates = name switch
        {
            "Visual Studio Code" => new[]
            {
                Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe"),
                Path.Combine(programs, "Microsoft VS Code", "Code.exe")
            },
            "Visual Studio" => Matching(Path.Combine(programs, "Microsoft Visual Studio"), "devenv.exe"),
            "JetBrains Rider" => Matching(Path.Combine(programs, "JetBrains"), "rider64.exe")
                .Prepend(Path.Combine(local, "Programs", "Rider", "bin", "rider64.exe")),
            "Notepad++" => new[] { Path.Combine(programs, "Notepad++", "notepad++.exe") },
            "Sublime Text" => new[] { Path.Combine(programs, "Sublime Text", "sublime_text.exe") },
            "Zed" => new[] { Path.Combine(local, "Programs", "Zed", "Zed.exe") },
            "Command Prompt" => new[] { Path.Combine(system, "cmd.exe") },
            "PowerShell" => new[] { Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe") },
            "Git Bash" => new[] { Path.Combine(programs, "Git", "git-bash.exe") },
            "Windows Terminal" => new[] { Path.Combine(local, "Microsoft", "WindowsApps", "wt.exe") },
            _ => Array.Empty<string>()
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    // A program somewhere under a folder (several versions of Visual Studio or Rider), newest folder first
    private static IEnumerable<string> Matching(string folder, string file)
    {
        try
        {
            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, file, SearchOption.AllDirectories).OrderDescending().ToList()
                : Enumerable.Empty<string>();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Enumerable.Empty<string>();
        }
    }

    // The program's first icon at the system's large size, copied out of Windows into a bitmap
    private static Bitmap? Extract(string program)
    {
        IntPtr icon = IntPtr.Zero;
        try
        {
            if (ExtractIconEx(program, 0, out icon, IntPtr.Zero, 1) == 0 || icon == IntPtr.Zero)
                return null;
            return ToBitmap(icon);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
                                              or ExternalException)
        {
            return null;
        }
        finally
        {
            if (icon != IntPtr.Zero)
                DestroyIcon(icon);
        }
    }

    private static Bitmap? ToBitmap(IntPtr icon)
    {
        if (!GetIconInfo(icon, out IconInfo info))
            return null;

        IntPtr screen = IntPtr.Zero;
        try
        {
            if (GetObject(info.Color, Marshal.SizeOf<BitmapHeader>(), out BitmapHeader header) == 0 ||
                header.Width <= 0 || header.Height <= 0)
                return null;

            int width = header.Width;
            int height = header.Height;
            BitmapInfo request = new BitmapInfo
            {
                Size = (uint)Marshal.SizeOf<BitmapInfo>(),
                Width = width,
                // Negative: rows from the top down, as Avalonia expects
                Height = -height,
                Planes = 1,
                BitCount = BYTES_PER_PIXEL * 8
            };
            byte[] pixels = new byte[width * height * BYTES_PER_PIXEL];
            screen = GetDC(IntPtr.Zero);
            if (GetDIBits(screen, info.Color, 0, (uint)height, pixels, ref request, DIB_RGB_COLORS) == 0)
                return null;

            // Old icons have no transparency of their own; they show fully opaque rather than invisible
            bool hasAlpha = false;
            for (int index = ALPHA_OFFSET; index < pixels.Length && !hasAlpha; index += BYTES_PER_PIXEL)
                hasAlpha = pixels[index] != 0;
            if (!hasAlpha)
            {
                for (int index = ALPHA_OFFSET; index < pixels.Length; index += BYTES_PER_PIXEL)
                    pixels[index] = OPAQUE;
            }

            WriteableBitmap bitmap = new WriteableBitmap(new PixelSize(width, height),
                                                         new Vector(SCREEN_DPI, SCREEN_DPI), PixelFormat.Bgra8888,
                                                         AlphaFormat.Unpremul);
            using ILockedFramebuffer frame = bitmap.Lock();
            for (int row = 0; row < height; row++)
                Marshal.Copy(pixels, row * width * BYTES_PER_PIXEL, frame.Address + row * frame.RowBytes,
                             width * BYTES_PER_PIXEL);
            return bitmap;
        }
        finally
        {
            if (screen != IntPtr.Zero)
                ReleaseDC(IntPtr.Zero, screen);
            if (info.Color != IntPtr.Zero)
                DeleteObject(info.Color);
            if (info.Mask != IntPtr.Zero)
                DeleteObject(info.Mask);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr Mask;
        public IntPtr Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, IntPtr small, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr handle, int size, out BitmapHeader header);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr context);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr context, IntPtr bitmap, uint start, uint lines, [Out] byte[] pixels,
                                        ref BitmapInfo info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
