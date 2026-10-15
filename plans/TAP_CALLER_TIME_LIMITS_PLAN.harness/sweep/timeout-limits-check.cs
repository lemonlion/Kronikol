// timeout-limits-check.cs, for plans/TAP_CALLER_TIME_LIMITS_PLAN.md (#173) R0 item 1 and §4.2 item 2: where the BCL stops
// taking a duration. Run: `dotnet run timeout-limits-check.cs`.
using System.Net.Http;
void Try(string what, Action a) { try { a(); Console.WriteLine($"{what}: ok"); } catch (Exception e) { Console.WriteLine($"{what}: {e.GetType().Name}: {e.Message}"); } }
Try("TimeSpan.MaxValue + 10s", () => { var x = TimeSpan.MaxValue + TimeSpan.FromSeconds(10); });
Try("HttpClient.Timeout = 25 days", () => { using var c = new HttpClient(); c.Timeout = TimeSpan.FromDays(25); });
Try("HttpClient.Timeout = 24 days", () => { using var c = new HttpClient(); c.Timeout = TimeSpan.FromDays(24); });
Try("CancelAfter(60 days)", () => { using var c = new CancellationTokenSource(); c.CancelAfter(TimeSpan.FromDays(60)); });
Try("CancelAfter(40 days)", () => { using var c = new CancellationTokenSource(); c.CancelAfter(TimeSpan.FromDays(40)); });
Try("SocketsHttpHandler.ConnectTimeout = 0", () => { using var h = new SocketsHttpHandler(); h.ConnectTimeout = TimeSpan.Zero; });
Try("SocketsHttpHandler.ConnectTimeout = MaxValue", () => { using var h = new SocketsHttpHandler(); h.ConnectTimeout = TimeSpan.MaxValue; });
Try("HttpClient.Timeout = Infinite + 10s", () => { using var c = new HttpClient(); c.Timeout = Timeout.InfiniteTimeSpan + TimeSpan.FromSeconds(10); Console.WriteLine("  -> " + c.Timeout); });
Console.WriteLine($"Stopwatch.Frequency={System.Diagnostics.Stopwatch.Frequency}");
Try("TimeProvider.System.CreateTimer(MaxValue)", () => { using var t = TimeProvider.System.CreateTimer(_ => {}, null, TimeSpan.FromDays(60), Timeout.InfiniteTimeSpan); });
