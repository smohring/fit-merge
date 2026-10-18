using System.Globalization;
using System.Windows;
using FitMerge.Services;

namespace FitMerge;

public partial class App : Application
{
    /// <summary>Product ID resolved from <c>--device</c>, or <c>null</c>.</summary>
    public static ushort? DeviceOverride { get; private set; }
    /// <summary>Serial number from <c>--serial</c>, or <c>null</c>.</summary>
    public static uint? SerialOverride { get; private set; }
    /// <summary>File paths passed on the command line.</summary>
    public static List<string> StartupFiles { get; } = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            ApplyLanguage(e.Args);
            ParseArgs(e.Args);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, Loc.Get("ArgErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        base.OnStartup(e);
    }

    /// <summary>Handles <c>--lang &lt;en|de&gt;</c> first, so every later message already uses that language.</summary>
    private static void ApplyLanguage(string[] args)
    {
        int i = Array.FindIndex(args, a => a.Equals("--lang", StringComparison.OrdinalIgnoreCase));
        if (i < 0) return;
        var value = Next(args, ref i, "--lang");
        try { Loc.SetLanguage(value); }
        catch (CultureNotFoundException) { throw new ArgumentException(Loc.Get("ArgUnknownLanguage", value)); }
    }

    /// <summary>Evaluates <c>--device &lt;name&gt;</c>, <c>--serial &lt;number&gt;</c> and file paths.</summary>
    private static void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--lang":
                    i++; // already applied by ApplyLanguage
                    break;
                case "--device" or "-d":
                    DeviceOverride = GarminProducts.Resolve(Next(args, ref i, "--device"));
                    break;
                case "--serial" or "-s":
                    var raw = Next(args, ref i, "--serial");
                    if (!uint.TryParse(raw, out var serial))
                        throw new ArgumentException(Loc.Get("ArgSerialInvalid", raw, uint.MaxValue));
                    SerialOverride = serial;
                    break;
                default:
                    StartupFiles.Add(args[i]);
                    break;
            }
        }
    }

    private static string Next(string[] args, ref int i, string option) =>
        ++i < args.Length ? args[i] : throw new ArgumentException(Loc.Get("ArgMissingValue", option));
}
