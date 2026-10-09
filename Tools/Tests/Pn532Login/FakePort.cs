using System.Collections.Concurrent;
using System.Diagnostics;
using MajdataPlay.Scenes.Login;

sealed class FakeClock
{
    long _seconds;
    public DateTime Now => new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Interlocked.Read(ref _seconds));
    public void Advance(int seconds) => Interlocked.Add(ref _seconds, seconds);
}
sealed class FakePorts
{
    public readonly ConcurrentQueue<FakePort> Opened = new();
    public Func<FakePort> Create = () => new FakePort();
    int _active, _max;
    public int Active => Volatile.Read(ref _active);
    public int Maximum => Volatile.Read(ref _max);
    public IPn532Port Open(CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        var active = Interlocked.Increment(ref _active);
        int before;
        do { before = Volatile.Read(ref _max); if (before >= active) break; }
        while (Interlocked.CompareExchange(ref _max, active, before) != before);
        var port = Create(); port.OnClose = () => Interlocked.Decrement(ref _active);
        Opened.Enqueue(port); return port;
    }
    public FakePort Last => Opened.LastOrDefault();
}
sealed class FakePort : IPn532Port
{
    readonly ConcurrentQueue<byte> _rx = new();
    readonly object _gate = new();
    public readonly List<byte> Commands = new();
    public readonly List<byte[]> Replies = new();
    public readonly List<string> Events = new();
    public Queue<byte[]> Apdus;
    public volatile bool Unplug;
    public bool Corrupt, NoAck, RepeatAck;
    public bool Closed { get; private set; }
    public bool Dtr { get; private set; }
    public Action OnClose;
    public int CommandsCount { get { lock (_gate) return Commands.Count; } }
    public void SetDtr(bool enabled) { lock (_gate) { Dtr = enabled; Events.Add(enabled ? "dtr-high" : "dtr-low"); } }
    public void ClearReceived() { lock (_gate) Events.Add("clear-rx"); while (_rx.TryDequeue(out _)) { } }
    void Queue(byte[] bytes) { foreach (var b in bytes) _rx.Enqueue(b); }
    static byte[] Response(byte command, byte[] data)
    {
        var frame = Pn532CdcDriver.Frame(command, data); frame[5] = 0xD5;
        frame[frame.Length - 2]--; return frame;
    }
    public void Write(byte[] frame)
    {
        if (Unplug) throw new IOException("fake_unplug");
        if (Closed) throw new ObjectDisposedException("fake");
        if (frame.SequenceEqual(new byte[] { 0x55, 0x55, 0, 0, 0 })) { lock (_gate) Events.Add("wake"); return; }
        if (frame.Length < 9 || frame[3] + frame[4] != 256 ||
            (frame.Skip(5).Take(frame.Length - 6).Sum(x => x) & 255) != 0) throw new Exception("bad_outbound_frame");
        byte command = frame[6];
        lock (_gate) { Commands.Add(command); Events.Add("command"); }
        if (NoAck) return;
        Queue(new byte[] { 0, 0, 0xFF, 0, 0xFF, 0 });
        if (RepeatAck) return;
        byte[] data;
        if (command == 0x8C)
        {
            if (Apdus == null) return;
            data = new byte[] { 8 };
        }
        else if (command == 0x86)
        {
            if (Apdus.Count == 0) return;
            data = new byte[] { 0 }.Concat(Apdus.Dequeue()).ToArray();
        }
        else if (command == 0x8E)
        {
            lock (_gate) Replies.Add(frame.Skip(7).Take(frame.Length - 9).ToArray());
            data = new byte[] { 0 };
        }
        else data = Array.Empty<byte>();
        var response = Response((byte)(command + 1), data);
        if (Corrupt) response[response.Length - 2]++;
        Queue(response);
    }
    public int Read(byte[] buffer)
    {
        if (Unplug) throw new IOException("fake_unplug");
        // Fragment every ACK/response down to three bytes.
        int count = 0;
        while (count < 3 && _rx.TryDequeue(out var b)) buffer[count++] = b;
        if (count == 0)
        {
            Thread.Sleep(2);
            if (RepeatAck) Queue(new byte[] { 0, 0, 0xFF, 0, 0xFF, 0 });
        }
        return count;
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (Closed) return;
            Dtr = false; Events.Add("close"); Closed = true;
        }
        while (_rx.TryDequeue(out _)) { }
        OnClose?.Invoke();
    }
}
