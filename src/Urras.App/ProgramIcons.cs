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

    private static readonly Dictionary<string, Task<Bitmap?>> Icons = new Dictionary<string, Task<Bitmap?>>();
    private static readonly Dictionary<string, string?> Found = new Dictionary<string, string?>();

    // The icon for a known editor or shell, read in the background; null when it isn't installed or can't be read
    public static Task<Bitmap?> ForAsync(string name)
    {
        if (!OperatingSystem.IsWindows())
            return Task.FromResult<Bitmap?>(null);

        lock (Icons)
        {
            if (!Icons.TryGetValue(name, out Task<Bitmap?>? icon))
            {
                icon = Task.Run(() => Find(name) is { } program ? Extract(program) : null);
                Icons[name] = icon;
            }

            return icon;
        }
    }

    // Whether Urras knows where this program installs, so a missing one can be told apart from a custom one
    public static bool IsKnown(string name)
    {
        return OperatingSystem.IsWindows() && Candidates(name).Any();
    }

    // The installed program, or null when it isn't in any of the places it usually goes
    public static string? Find(string name)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        lock (Found)
        {
            if (!Found.TryGetValue(name, out string? program))
            {
                program = Candidates(name).FirstOrDefault(File.Exists);
                Found[name] = program;
            }

            return program;
        }
    }

    // Where each known program is usually installed, in order of preference; only these places are looked at
    private static IEnumerable<string> Candidates(string name)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programs32 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string[] roots = { programs, programs32 };
        return name switch
        {
            "Visual Studio Code" => new[]
            {
                Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe"),
                Path.Combine(programs, "Microsoft VS Code", "Code.exe")
            },
            "Visual Studio Code Insiders" => new[]
            {
                Path.Combine(local, "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe"),
                Path.Combine(programs, "Microsoft VS Code Insiders", "Code - Insiders.exe")
            },
            "VSCodium" => new[]
            {
                Path.Combine(local, "Programs", "VSCodium", "VSCodium.exe"),
                Path.Combine(programs, "VSCodium", "VSCodium.exe")
            },
            "Cursor" => new[] { Path.Combine(local, "Programs", "cursor", "Cursor.exe") },
            "Windsurf" => new[] { Path.Combine(local, "Programs", "Windsurf", "Windsurf.exe") },
            // Program Files\Microsoft Visual Studio\<version>\<edition>\Common7\IDE\devenv.exe
            "Visual Studio" => roots.SelectMany(root => Under(Path.Combine(root, "Microsoft Visual Studio"), 2,
                                                              Path.Combine("Common7", "IDE", "devenv.exe"))),
            "JetBrains Rider" => JetBrains(local, roots, "Rider", "rider64.exe"),
            "IntelliJ IDEA" => JetBrains(local, roots, "IntelliJ IDEA", "idea64.exe"),
            "CLion" => JetBrains(local, roots, "CLion", "clion64.exe"),
            "Notepad++" => roots.Select(root => Path.Combine(root, "Notepad++", "notepad++.exe")),
            "Sublime Text" => roots.Select(root => Path.Combine(root, "Sublime Text", "sublime_text.exe")),
            "Zed" => new[] { Path.Combine(local, "Programs", "Zed", "Zed.exe") },
            "Command Prompt" => new[] { Path.Combine(system, "cmd.exe") },
            "PowerShell" => new[] { Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe") },
            "PowerShell 7" => new[] { Path.Combine(programs, "PowerShell", "7", "pwsh.exe") },
            "Git Bash" => roots.Select(root => Path.Combine(root, "Git", "git-bash.exe")),
            "WSL" => new[] { Path.Combine(system, "wsl.exe") },
            "Windows Terminal" => new[] { Path.Combine(local, "Microsoft", "WindowsApps", "wt.exe") },
            _ => Array.Empty<string>()
        };
    }

    // A JetBrains IDE from the Toolbox (in the user's programs) or a standalone install (Program Files\JetBrains)
    private static IEnumerable<string> JetBrains(string local, string[] roots, string product, string program)
    {
        string inBin = Path.Combine("bin", program);
        return Under(Path.Combine(local, "Programs"), 1, inBin)
            .Where(path => path.Contains(product, StringComparison.OrdinalIgnoreCase))
            .Concat(roots.SelectMany(root => Under(Path.Combine(root, "JetBrains"), 1, inBin)
                                             .Where(path => path.Contains(product,
                                                                          StringComparison.OrdinalIgnoreCase))));
    }

    // relative inside each folder exactly depth levels below folder, newest-named first (2026 before 2022)
    private static IEnumerable<string> Under(string folder, int depth, string relative)
    {
        try
        {
            IEnumerable<string> level = new[] { folder };
            for (int step = 0; step < depth; step++)
                level = level.Where(Directory.Exists).SelectMany(Directory.EnumerateDirectories).ToList();
            return level.OrderDescending().Select(found => Path.Combine(found, relative)).ToList();
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
