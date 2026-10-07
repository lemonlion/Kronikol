using System.Globalization;

namespace Kronikol.Tests;

/// <summary>
/// Runs code with the thread's culture set to one that formats numbers and dates differently from en-US, then puts the
/// previous culture back. The culture flows into the tasks the code starts (it is part of the execution context), so a
/// writer that fans out with <c>Parallel.Invoke</c> formats under it too.
/// </summary>
internal static class CultureRun
{
    /// <summary>
    /// One culture for each assumption a writer can make about the machine's: a decimal comma and a full stop between
    /// thousands (de-DE); a full stop between hours and minutes and U+2212 as the minus sign (fi-FI); a Buddhist
    /// calendar, so the year 2026 is 2569 (th-TH); the Umm al-Qura calendar, Arabic decimal and thousands separators and
    /// a minus sign led by a letter mark (ar-SA).
    /// </summary>
    public static readonly string[] Cultures = ["de-DE", "fi-FI", "th-TH", "ar-SA"];

    public static TheoryData<string> Data()
    {
        var data = new TheoryData<string>();
        foreach (var culture in Cultures)
            data.Add(culture);
        return data;
    }

    public static T Under<T>(string culture, Func<T> body)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    public static void Under(string culture, Action body) => Under(culture, () =>
    {
        body();
        return 0;
    });
}
