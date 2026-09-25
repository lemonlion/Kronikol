using System.Globalization;

namespace Kronikol.PlantUml;

/// <summary>WCAG 2.1 contrast between two <c>#RRGGBB</c> colours.</summary>
internal static class WcagContrast
{
    /// <summary>The contrast ratio, from 1 to 21: (Lmax + 0.05) / (Lmin + 0.05).</summary>
    internal static double Ratio(string foreground, string background)
    {
        var a = Luminance(foreground);
        var b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>
    /// Walks a neutral grey down from channel value <paramref name="from"/> and returns the first whose lowest
    /// ratio over <paramref name="fills"/> is at or above <paramref name="floor"/>, so the answer is never
    /// lighter than where the walk starts. Black when nothing clears the floor.
    /// </summary>
    internal static string LightestGreyClearing(double floor, int from, params string[] fills)
    {
        for (var g = from; g >= 0; g--)
        {
            var channel = g.ToString("X2", CultureInfo.InvariantCulture);
            var grey = "#" + channel + channel + channel;
            if (fills.All(fill => Ratio(grey, fill) >= floor)) return grey;
        }
        return "#000000";
    }

    /// <summary>Relative luminance, with the sRGB channels linearised.</summary>
    private static double Luminance(string hex)
    {
        double Channel(int start)
        {
            var c = int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }
}
