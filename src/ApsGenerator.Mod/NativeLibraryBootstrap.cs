using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ApsGenerator.Mod;

internal static class NativeLibraryBootstrap
{
    private const string LinuxLibraryFileName = "libcryptominisat5.so";
    private const string GmpLibraryFileName = "libgmp.so.10";
    private const string GmpCppLibraryFileName = "libgmpxx.so.4";
    private const string WindowsLibraryFileName = "cryptominisat5.dll";
    private const string MacLibraryFileName = "libcryptominisat5.dylib";
    private const string LinuxDynamicLoader = "libdl.so.2";
    // dyld resolves this from the shared cache even though the file is absent on disk.
    private const string MacDynamicLoader = "/usr/lib/libSystem.B.dylib";
    private const int MacLoadNow = 0x2;
    private const int MacLoadGlobal = 0x8;
    private const string WindowsDynamicLoader = "kernel32.dll";
    private const int LoadImmediately = 2;
    private const int ExportSymbolsGlobally = 0x100;
    private const uint LoadLibrarySearchDllLoadDirectory = 0x00000100;
    private const uint LoadLibrarySearchDefaultDirectories = 0x00001000;

    private static nint gmpLibraryHandle;
    private static nint gmpCppLibraryHandle;
    private static nint nativeLibraryHandle;

    internal static void LoadFromModDirectory(Assembly modAssembly)
    {
        EnsureSupportedPlatform();

        string assemblyPath = modAssembly.Location;
        string? modDirectory = Path.GetDirectoryName(assemblyPath);
        if (string.IsNullOrWhiteSpace(modDirectory))
            throw new InvalidOperationException("The APS Generator mod directory could not be resolved.");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            nativeLibraryHandle = LoadWindowsLibrary(
                Path.Combine(modDirectory, WindowsLibraryFileName), nativeLibraryHandle);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // The macOS build is universal (arm64 + x86_64) and links only system libraries.
            nativeLibraryHandle = LoadMacLibrary(
                Path.Combine(modDirectory, MacLibraryFileName), nativeLibraryHandle);
            return;
        }

        string packagedGmpPath = Path.Combine(modDirectory, GmpLibraryFileName);
        string gmpPath = File.Exists(packagedGmpPath) ? packagedGmpPath : GmpLibraryFileName;
        gmpLibraryHandle = LoadLinuxLibrary(
            gmpPath,
            gmpLibraryHandle,
            "the GMP runtime",
            requirePackagedFile: false);
        gmpCppLibraryHandle = LoadLinuxLibrary(
            Path.Combine(modDirectory, GmpCppLibraryFileName),
            gmpCppLibraryHandle,
            "the GMP C++ runtime");
        nativeLibraryHandle = LoadLinuxLibrary(
            Path.Combine(modDirectory, LinuxLibraryFileName),
            nativeLibraryHandle,
            "CryptoMiniSat");
    }

    private static void EnsureSupportedPlatform()
    {
        Architecture architecture = RuntimeInformation.ProcessArchitecture;
        bool supported = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? architecture is Architecture.X64 or Architecture.Arm64
            : (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
              && architecture == Architecture.X64;
        if (!supported)
            throw new PlatformNotSupportedException(
                "APS Generator currently supports Linux x64, Windows x64, and macOS (Apple silicon and Intel) only.");
    }

    private static nint LoadMacLibrary(string libraryPath, nint existingHandle)
    {
        if (existingHandle != IntPtr.Zero)
            return existingHandle;

        if (!File.Exists(libraryPath))
            throw new FileNotFoundException("The packaged CryptoMiniSat library was not found.", libraryPath);

        nint loadedHandle = MacDlopen(libraryPath, MacLoadNow | MacLoadGlobal);
        if (loadedHandle != IntPtr.Zero)
            return loadedHandle;

        nint errorPointer = MacDlerror();
        string loaderError = errorPointer == IntPtr.Zero
            ? "the platform loader did not provide an error"
            : Marshal.PtrToStringAnsi(errorPointer) ?? "unknown platform loader error";
        throw new DllNotFoundException($"CryptoMiniSat could not be loaded from '{libraryPath}': {loaderError}");
    }

    private static nint LoadLinuxLibrary(
        string libraryPath,
        nint existingHandle,
        string libraryName,
        bool requirePackagedFile = true)
    {
        if (existingHandle != IntPtr.Zero)
            return existingHandle;

        if (requirePackagedFile && !File.Exists(libraryPath))
            throw new FileNotFoundException($"The packaged {libraryName} library was not found.", libraryPath);

        nint loadedHandle = Dlopen(
            libraryPath,
            LoadImmediately | ExportSymbolsGlobally);
        if (loadedHandle != IntPtr.Zero)
            return loadedHandle;

        string loaderError = ReadLoaderError();
        throw new DllNotFoundException(
            $"{libraryName} could not be loaded from '{libraryPath}': {loaderError}");
    }

    private static nint LoadWindowsLibrary(string libraryPath, nint existingHandle)
    {
        if (existingHandle != IntPtr.Zero)
            return existingHandle;

        if (!File.Exists(libraryPath))
            throw new FileNotFoundException("The packaged CryptoMiniSat library was not found.", libraryPath);

        nint loadedHandle = LoadLibraryEx(
            libraryPath,
            IntPtr.Zero,
            LoadLibrarySearchDllLoadDirectory | LoadLibrarySearchDefaultDirectories);
        if (loadedHandle != IntPtr.Zero)
            return loadedHandle;

        throw new DllNotFoundException(
            $"CryptoMiniSat could not be loaded from '{libraryPath}': {new Win32Exception().Message}");
    }

    private static string ReadLoaderError()
    {
        nint errorPointer = Dlerror();
        return errorPointer == IntPtr.Zero
            ? "the platform loader did not provide an error"
            : Marshal.PtrToStringAnsi(errorPointer) ?? "unknown platform loader error";
    }

    [DllImport(
        LinuxDynamicLoader,
        EntryPoint = "dlopen",
        CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi)]
    private static extern nint Dlopen(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName,
        int flags);

    [DllImport(LinuxDynamicLoader, EntryPoint = "dlerror", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint Dlerror();

    [DllImport(MacDynamicLoader, EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint MacDlopen([MarshalAs(UnmanagedType.LPUTF8Str)] string fileName, int flags);

    [DllImport(MacDynamicLoader, EntryPoint = "dlerror", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint MacDlerror();

    [DllImport(WindowsDynamicLoader, EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryEx(string fileName, nint fileHandle, uint flags);
}
