namespace Kronikol.Tests.TcpTap;

/// <summary>
/// <see cref="TcpTapLocalhostTests"/> runs here, because one of its facts asks a port it has just freed to refuse a connection.
/// </summary>
/// <remarks>
/// Beside the rest of this suite, a listener another test opens can be handed that port, and the port then answers. CI's Linux
/// run read an empty answer there on 5.28.4's commit, and the same commit passed when that job ran again. <c>DisableParallelization</c>
/// runs the class with nothing else of this suite beside it.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FreedPortCollection
{
    public const string Name = "FreedPort";
}
