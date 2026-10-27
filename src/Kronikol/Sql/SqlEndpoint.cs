using System.Globalization;
using System.Text.RegularExpressions;

namespace Kronikol.Sql;

/// <summary>
/// The parts of a <c>sql://</c> arrow's address made from what a connection reports, and the address built from them.
/// </summary>
/// <remarks>
/// A connection's data source goes into the address as its host, which it often is not: SQL Server's <c>host,port</c>,
/// LocalDB's <c>(localdb)\MSSQLLocalDB</c> and every named instance, a protocol prefix (<c>tcp:</c>, <c>np:</c>),
/// Npgsql's <c>tcp://host:port</c> and its host lists, a pipe, a socket or a file path. Until 5.11.1 the trackers put it
/// into <c>new Uri</c> as it was, so for most of those every command failed with a <see cref="UriFormatException"/>, and
/// every Npgsql arrow drawn by the EF Core interceptor named a host called <c>tcp</c>.
/// </remarks>
internal static partial class SqlEndpoint
{
    // Longest first: "tcp://" before "tcp:".
    private static readonly string[] ProtocolPrefixes = ["tcp://", "unix:", "tcp:", "np:", "lpc:", "admin:"];

    /// <summary>
    /// The host an arrow is drawn against: <c>host:port</c> for SQL Server's <c>host,port</c>, the first host of a list,
    /// without a protocol prefix, an instance name joined to its server with a dash (<c>localdb-mssqllocaldb</c>), a
    /// bracketed IPv6 address as it is, and <c>localhost</c> for a pipe, a socket, a file path or anything that cannot be
    /// a host. Always a valid host for <c>{scheme}://{host}/</c>.
    /// </summary>
    internal static string Host(string? dataSource)
    {
        var text = dataSource?.Trim() ?? "";
        foreach (var prefix in ProtocolPrefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            text = text[prefix.Length..].Trim();
            break;
        }

        // A pipe (\\.\pipe\…), a socket directory or a file names no host: the server is this machine.
        if (text.Length == 0 || text.StartsWith(@"\\", StringComparison.Ordinal) || text.StartsWith('/') || DrivePath().IsMatch(text))
            return "localhost";

        var comma = text.IndexOf(',');
        if (comma >= 0)
        {
            var after = text[(comma + 1)..].Trim();
            text = after.Length > 0 && after.All(char.IsAsciiDigit)
                ? $"{text[..comma].Trim()}:{after}" // SQL Server's host,port
                : text[..comma].Trim();             // the first of a host list
        }

        var bracketed = BracketedAddress().Match(text);
        if (bracketed.Success)
            return Uri.CheckHostName(bracketed.Groups["address"].Value) == UriHostNameType.IPv6
                ? WithPort($"[{bracketed.Groups["address"].Value}]", bracketed.Groups["port"].Value)
                : "localhost";

        string? port = null;
        var withPort = HostAndPort().Match(text);
        if (withPort.Success)
        {
            text = withPort.Groups["host"].Value;
            port = withPort.Groups["port"].Value;
        }

        var host = Uri.CheckHostName(text) switch
        {
            UriHostNameType.Dns or UriHostNameType.IPv4 => text,
            UriHostNameType.IPv6 => $"[{text}]",
            _ when text.Contains('\\') => InstanceHost(text),
            _ when text.Equals("(local)", StringComparison.OrdinalIgnoreCase) => "localhost",
            _ when text.Contains(':') => "localhost",
            _ => ValidOrLocalhost(Clean(text)),
        };
        return WithPort(host, port);
    }

    /// <summary>A path segment (a database or table name) as written, escaped for the address.</summary>
    internal static string Segment(string? name) => Uri.EscapeDataString(string.IsNullOrEmpty(name) ? "unknown" : name);

    /// <summary>The address <paramref name="text"/> names, or <c>{scheme}://localhost/</c> when it does not parse.</summary>
    internal static Uri Build(string text, string scheme = "sql")
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri)) return uri;
        return Uri.TryCreate($"{scheme}://localhost", UriKind.Absolute, out var fallback) ? fallback : new Uri("sql://localhost");
    }

    private static string InstanceHost(string text)
    {
        var parts = text.Split('\\', 2);
        var server = parts[0].Trim() switch
        {
            "" or "." or "(local)" => "localhost",
            var name when name.Equals("(localdb)", StringComparison.OrdinalIgnoreCase) => "localdb",
            var name => Clean(name),
        };
        var instance = Clean(parts[1]);
        if (server.Length == 0) server = "localhost";
        return ValidOrLocalhost(instance.Length == 0 ? server : $"{server}-{instance}");
    }

    private static string Clean(string name) => NotHostCharacters().Replace(name.Trim(), "-").Trim('-', '.');

    private static string ValidOrLocalhost(string host) =>
        Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4 ? host : "localhost";

    private static string WithPort(string host, string? port) =>
        int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is > 0 and <= 65535
            ? $"{host}:{number.ToString(CultureInfo.InvariantCulture)}"
            : host;

    [GeneratedRegex(@"^[A-Za-z]:[\\/]")]
    private static partial Regex DrivePath();

    [GeneratedRegex(@"^\[(?<address>[^\]]+)\](?::(?<port>\d{1,5}))?$")]
    private static partial Regex BracketedAddress();

    [GeneratedRegex(@"^(?<host>[^:]*):(?<port>\d{1,5})$")]
    private static partial Regex HostAndPort();

    [GeneratedRegex(@"[^A-Za-z0-9.-]+")]
    private static partial Regex NotHostCharacters();
}
