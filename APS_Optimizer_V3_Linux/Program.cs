using System;

namespace APS_Optimizer_V3_Linux;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // Handle Linux display scaling issues
            ConfigureLinuxDisplayScaling();

            var host = new Uno.UI.Runtime.Skia.Gtk.GtkHost(() => new APS_Optimizer_V3.App());
            host.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("GTK host failed: " + ex);
        }
    }

    private static void ConfigureLinuxDisplayScaling()
    {
        // Check if scale override is already set
        var currentOverride = Environment.GetEnvironmentVariable("UNO_DISPLAY_SCALE_OVERRIDE");
        if (!string.IsNullOrEmpty(currentOverride))
        {
            Console.WriteLine($"Using existing display scale override: {currentOverride}");
            return;
        }

        // Detect if we're running under X11 with potential scaling issues
        var displayVar = Environment.GetEnvironmentVariable("DISPLAY");
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");

        if (!string.IsNullOrEmpty(displayVar) && string.IsNullOrEmpty(waylandDisplay))
        {
            // Running under X11 - set default scale to prevent common issues
            Console.WriteLine("Detected X11 environment. Setting default display scale to 1.0 to prevent scaling issues.");
            Console.WriteLine("If UI appears too small/large, set UNO_DISPLAY_SCALE_OVERRIDE environment variable (e.g., 1.25, 1.5, 2.0)");
            Environment.SetEnvironmentVariable("UNO_DISPLAY_SCALE_OVERRIDE", "1.0");
        }
    }
}
