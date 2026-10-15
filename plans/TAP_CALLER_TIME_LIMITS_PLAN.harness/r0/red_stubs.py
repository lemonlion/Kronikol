root = 'C:/Code/Kronikol-173-red/src/'


def edit(name, pairs):
    p = root + name
    s = open(p, encoding='utf-8', newline='').read()
    for old, new in pairs:
        assert s.count(old) == 1, (name, old[:80], s.count(old))
        s = s.replace(old, new)
    open(p, 'w', encoding='utf-8', newline='').write(s)


# The seams R0's facts read, with 4.22.1's behaviour: no new behaviour, only a way to see the old one.
edit('Kronikol.Extensions.ProxyTap/ProxyTap.cs', [(
    '''    internal int InFlight => Volatile.Read(ref _inflight);
''',
    '''    internal int InFlight => Volatile.Read(ref _inflight);

    // Seam for the red proof: 4.22.1 has no stopping phase of its own, so the tap is stopping once DisposeAsync has begun.
    internal bool Stopping => Volatile.Read(ref _disposed) == 1;
''')])

edit('Kronikol.Extensions.Otlp/HttpRequestReader.cs', [(
    '''    private int _start;
    private int _end;
''',
    '''    private int _start;
    private int _end;
    private int _bodyBytesHeld;

    // Seam for the red proof: the size of the array 4.22.1 makes for a body.
    internal int BodyBytesHeld => Volatile.Read(ref _bodyBytesHeld);
'''), (
    '''        var result = new byte[count];
''',
    '''        var result = new byte[count];
        Volatile.Write(ref _bodyBytesHeld, count);
''')])
print('ok')
