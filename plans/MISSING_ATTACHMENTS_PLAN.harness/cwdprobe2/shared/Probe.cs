// Shared by every probe project (linked, not copied). Records where a relative path written by a test lands.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CwdProbeShared
{
    internal static class ProbeLog
    {
        private const string Folder = "<scratch>/cwdprobe2/";
        public const string ResultsPath = Folder + "results.txt";
        public const string DetailsPath = Folder + "details.txt";

        internal static string? CwdAtModuleInit;
        internal static string FrameworkLabel = "unknown";

        public static string Mode => Environment.GetEnvironmentVariable("CWDPROBE_MODE") ?? "unset";

        public static void RecordTest(string framework, Type frameworkMarker)
        {
            FrameworkLabel = framework + " " + VersionOf(frameworkMarker.Assembly);
            string cwd = Environment.CurrentDirectory;
            string baseDir = AppContext.BaseDirectory;
            string domainBase = AppDomain.CurrentDomain.BaseDirectory;
            bool same = SamePath(cwd, baseDir);
            Append(ResultsPath, $"{FrameworkLabel}|{Mode}|cwd={cwd}|base={baseDir}|domainBase={domainBase}|same={(same ? "true" : "false")}");

            string loaded = string.Join(";", AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetName().Name ?? "")
                .Where(n => n.Contains("TestAdapter", StringComparison.OrdinalIgnoreCase)
                         || n.Contains("Testing.Platform", StringComparison.OrdinalIgnoreCase)
                         || n.Contains("testhost", StringComparison.OrdinalIgnoreCase)
                         || n.Contains("TestPlatform", StringComparison.OrdinalIgnoreCase)
                         || n.Contains("VSTestBridge", StringComparison.OrdinalIgnoreCase)
                         || n.StartsWith("nunit", StringComparison.OrdinalIgnoreCase)
                         || n.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
                         || n.StartsWith("MSTest", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(n => n, StringComparer.Ordinal)
                .Select(n => n + "@" + VersionOf(AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == n))));
            Append(DetailsPath, $"{FrameworkLabel}|{Mode}|phase=test|cwd={cwd}|cwdAtModuleInit={CwdAtModuleInit}|sameAtModuleInit={(CwdAtModuleInit is null ? "n/a" : SamePath(CwdAtModuleInit, baseDir) ? "true" : "false")}|process={Environment.ProcessPath}|pid={Environment.ProcessId}|commandLine={Environment.CommandLine}|loaded={loaded}");
        }

        public static string VersionOf(Assembly assembly)
        {
            string? info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(info))
            {
                return assembly.GetName().Version?.ToString() ?? "?";
            }

            int plus = info.IndexOf('+');
            return plus >= 0 ? info.Substring(0, plus) : info;
        }

        public static bool SamePath(string a, string b) =>
            string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        public static void Append(string path, string line)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.AppendAllText(path, line + Environment.NewLine);
                    return;
                }
                catch (IOException) when (attempt < 100)
                {
                    Thread.Sleep(20);
                }
            }
        }
    }

    internal static class ProbeModuleInit
    {
#pragma warning disable CA2255 // a probe, not a library
        [ModuleInitializer]
#pragma warning restore CA2255
        internal static void Init()
        {
            ProbeLog.CwdAtModuleInit = Environment.CurrentDirectory;
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    ProbeLog.Append(ProbeLog.DetailsPath, $"{ProbeLog.FrameworkLabel}|{ProbeLog.Mode}|phase=processexit|cwd={Environment.CurrentDirectory}|pid={Environment.ProcessId}");
                }
                catch
                {
                    // best effort only
                }
            };
        }
    }
}
