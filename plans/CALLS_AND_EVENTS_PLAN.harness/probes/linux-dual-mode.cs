using System.Net;
using System.Net.Sockets;

Report("::1, dual mode asked for", IPAddress.IPv6Loopback, dual: true);
Report("::1, nothing asked for", IPAddress.IPv6Loopback, dual: false);
Report("::, dual mode asked for", IPAddress.IPv6Any, dual: true);
Report("::, nothing asked for", IPAddress.IPv6Any, dual: false);

static void Report(string label, IPAddress address, bool dual)
{
    var listener = new TcpListener(address, 0);
    if (dual)
        listener.Server.DualMode = true;
    var before = listener.Server.DualMode;
    listener.Start();
    Console.WriteLine($"{label}: DualMode reads {before} before the bind, {listener.Server.DualMode} after it");
    listener.Stop();
}
