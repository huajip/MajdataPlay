using System.Diagnostics;
using System.Text;
using MajdataPlay.Scenes.Login;

static class Tests
{
    internal const string Url = "https://login.example/machine/authorize?auth-id=fake-1";
    static int _passed;
    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _passed++; Console.WriteLine("PASS " + message);
    }
    internal static async Task Until(Func<bool> condition, int milliseconds = 3000)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.ElapsedMilliseconds >= milliseconds) throw new TimeoutException("test_condition_timeout");
            await Task.Delay(5);
        }
    }
    internal static QrNfcPublisher Publisher(FakePorts ports, FakeClock clock) =>
        new QrNfcPublisher(ports.Open, () => clock.Now, "login.example", "/machine/authorize");
    static async Task Idle(QrNfcPublisher publisher, int milliseconds = 3000) =>
        await publisher.WhenIdleAsync().WaitAsync(TimeSpan.FromMilliseconds(milliseconds));
    static bool Released(FakePort port) => port.Closed && !port.Dtr &&
        port.Events.Last() == "close" && port.Events.IndexOf("dtr-low") < port.Events.IndexOf("close");
    internal static byte[] Select(byte[] data, byte p1) => new byte[] { 0, 0xA4, p1, 0x0C, (byte)data.Length }.Concat(data).ToArray();
    internal static byte[] Read(int offset, int count) => new byte[] { 0, 0xB0, (byte)(offset >> 8), (byte)offset, (byte)count };
    static readonly byte[] Aid = { 0xD2, 0x76, 0, 0, 0x85, 1, 1 };
    static void Type4()
    {
        var ndef = Pn532Type4.UriRecord(Url);
        Check(ndef[0] == 0xD1 && ndef[3] == 0x55 && ndef[4] == 4 &&
            "https://" + Encoding.UTF8.GetString(ndef.Skip(5).ToArray()) == Url, "URI record preserves the supplied HTTPS URL");
        var longUrl = Url + new string('x', 300);
        var longNdef = Pn532Type4.UriRecord(longUrl);
        Check(longNdef[0] == 0xC1 && longNdef[6] == 0x55 && longNdef[7] == 4 &&
            "https://" + Encoding.UTF8.GetString(longNdef.Skip(8).ToArray()) == longUrl, "long URI uses a normal NDEF record");
        using var tag = new Pn532Type4(ndef);
        Check(tag.Process(Read(0, 2)).SequenceEqual(new byte[] { 0x69, 0x86 }), "READ before selecting a file is denied");
        Check(tag.Process(Select(Aid, 4)).SequenceEqual(new byte[] { 0x90, 0 }), "standard NDEF application selects successfully");
        tag.Process(Select(new byte[] { 0xE1, 3 }, 0));
        var cc = tag.Process(Read(0, 15));
        Check(cc.Length == 17 && cc[2] == 0x20 && cc[4] == 51 && cc[9] == 0xE1 && cc[10] == 4 && cc[13] == 0 && cc[14] == 0xFF, "capability container advertises a read-only Type 4 file");
        tag.Process(Select(new byte[] { 0xE1, 4 }, 0));
        var nlen = tag.Process(Read(0, 2));
        Check(nlen[0] * 256 + nlen[1] == ndef.Length, "NLEN matches the NDEF body");
        var result = new List<byte>();
        for (int offset = 0; offset < ndef.Length; offset += 51)
        {
            var reply = tag.Process(Read(2 + offset, Math.Min(51, ndef.Length - offset)));
            Check(reply.TakeLast(2).SequenceEqual(new byte[] { 0x90, 0 }), "chunked READ returns success");
            result.AddRange(reply.Take(reply.Length - 2));
        }
        Check(result.SequenceEqual(ndef), "chunked Type 4 reads reconstruct the exact NDEF");
        Check(tag.Process(new byte[] { 0, 0xD6, 0, 0, 0 }).SequenceEqual(new byte[] { 0x69, 0x82 }), "NDEF writes are denied");
        Check(tag.Process(Select(new byte[] { 0 }, 4)).SequenceEqual(new byte[] { 0x6A, 0x82 }) &&
            tag.Process(Read(0, 2)).SequenceEqual(new byte[] { 0x69, 0x86 }), "failed application selection invalidates prior file state");
    }
    static async Task Transport()
    {
        var ports = new FakePorts(); var clock = new FakeClock();
        using var publisher = Publisher(ports, clock);
        Check(ports.Active == 0, "constructing a publisher does not occupy a device");
        publisher.Publish(Url);
        Check(await publisher.WhenPreparedAsync(), "fragmented PN532 ACK arms the target");
        var first = ports.Last;
        Check(first.Commands.SequenceEqual(new byte[] { 0x14, 0x32, 0x12, 0x8C }), "CDC initialization uses the target-mode command sequence");
        Check(first.Events.Take(3).SequenceEqual(new[] { "dtr-high", "wake", "clear-rx" }), "one wake and receive clear occur before commands");
        for (int i = 0; i < 40; i++) { clock.Advance(2); publisher.ConfirmPending(publisher.BeginCheck()); }
        Check(ports.Opened.Count == 1 && first.CommandsCount == 4 && !first.Closed, "202 renewals keep one target worker alive beyond sixty seconds");
        var oldCheck = publisher.BeginCheck();
        publisher.Publish(Url.Replace("fake-1", "fake-2"));
        Check(await publisher.WhenPreparedAsync(), "QR refresh reopens after the old worker releases");
        Check(Released(first) && ports.Maximum == 1, "QR replacement lowers DTR and closes before reopening");
        clock.Advance(9); publisher.ConfirmPending(oldCheck);
        await Idle(publisher);
        Check(Released(ports.Last), "an old 202 cannot renew a replacement QR");
        publisher.Publish(Url);
        Check(await publisher.WhenPreparedAsync(), "a new server URL can start after proof expiry");
        publisher.Clear(); await Idle(publisher);
        Check(Released(ports.Last) && ports.Active == 0, "clear releases DTR and the port");
        publisher.Publish(Url); await publisher.WhenPreparedAsync();
        publisher.DisposeAndFlush();
        Check(ports.Active == 0 && Released(ports.Last), "scene shutdown flushes device release");
        publisher.Publish(Url);
        Check(ports.Active == 0, "late publish after disposal cannot reopen");

        var apdus = new Queue<byte[]>();
        apdus.Enqueue(Select(Aid, 4)); apdus.Enqueue(Select(new byte[] { 0xE1, 3 }, 0)); apdus.Enqueue(Read(0, 15));
        apdus.Enqueue(Select(new byte[] { 0xE1, 4 }, 0)); apdus.Enqueue(Read(0, 2));
        var expected = Pn532Type4.UriRecord(Url);
        for (int offset = 0; offset < expected.Length; offset += 51) apdus.Enqueue(Read(offset + 2, Math.Min(51, expected.Length - offset)));
        ports = new FakePorts { Create = () => new FakePort { Apdus = apdus } };
        using (var endToEnd = Publisher(ports, new FakeClock()))
        {
            endToEnd.Publish(Url); await endToEnd.WhenPreparedAsync();
            await Until(() => ports.Last.Replies.Count == 5 + (expected.Length + 50) / 51);
            var readBytes = ports.Last.Replies.Skip(5).SelectMany(x => x.Take(x.Length - 2));
            Check(readBytes.SequenceEqual(expected), "raw PN532 frames carry ISO-DEP APDUs and the exact NDEF response");
            Check(ports.Last.Events.Count(x => x == "wake") == 1 && ports.Last.Events.Count(x => x == "clear-rx") == 1, "APDU reads and writes never wake or discard the transport");
            endToEnd.Clear(); await Idle(endToEnd);
            Check(Released(ports.Last), "APDU session cleanup releases the device");
        }
    }
    static async Task Failures()
    {
        var ports = new FakePorts(); var clock = new FakeClock();
        using (var publisher = Publisher(ports, clock))
        {
            publisher.Publish(Url); await publisher.WhenPreparedAsync();
            ports.Last.Unplug = true;
            await Idle(publisher);
            Check(publisher.Faulted && Released(ports.Last), "unplug faults the current QR and releases the handle");
            var count = ports.Opened.Count;
            publisher.ConfirmPending(publisher.BeginCheck()); publisher.Tick();
            await Task.Delay(20);
            Check(ports.Opened.Count == count, "server polling does not replay a failed QR after unplug");
            publisher.Publish(Url.Replace("fake-1", "fake-2"));
            Check(await publisher.WhenPreparedAsync(), "a newly issued QR can retry after reconnect");
            publisher.Clear(); await Idle(publisher);
        }
        ports = new FakePorts { Create = () => new FakePort { Corrupt = true } };
        using (var bad = Publisher(ports, new FakeClock()))
        {
            bad.Publish(Url); Check(!await bad.WhenPreparedAsync(), "bad checksums fail before arming");
            await Idle(bad); Check(bad.Faulted && Released(ports.Last), "invalid protocol data closes the port");
        }
        ports = new FakePorts { Create = () => new FakePort { NoAck = true } };
        using (var blocked = Publisher(ports, new FakeClock()))
        {
            blocked.Publish(Url); await Until(() => ports.Last?.CommandsCount == 1);
            blocked.DisposeAndFlush();
            Check(Released(ports.Last), "teardown cancels a command with no ACK");
        }
        ports = new FakePorts { Create = () => new FakePort { RepeatAck = true } };
        using (var flood = Publisher(ports, new FakeClock()))
        {
            var timer = Stopwatch.StartNew(); flood.Publish(Url); await Idle(flood, 7000);
            Check(flood.Faulted && Released(ports.Last) && timer.ElapsedMilliseconds < 6500, "repeated ACKs do not reset the five-second command deadline");
        }
        ports = new FakePorts(); clock = new FakeClock();
        using (var expiry = Publisher(ports, clock))
        {
            expiry.Publish(Url); await expiry.WhenPreparedAsync(); clock.Advance(9);
            // No Unity Tick: the transport itself must stop expired emulation.
            await Idle(expiry);
            Check(Released(ports.Last), "server-proof expiry releases the device without a scene tick");
        }
        ports = new FakePorts(); clock = new FakeClock();
        using (var delayed = Publisher(ports, clock))
        {
            delayed.Publish(Url); await delayed.WhenPreparedAsync();
            var check = delayed.BeginCheck(); clock.Advance(9); delayed.ConfirmPending(check);
            await Idle(delayed);
            Check(Released(ports.Last), "a 202 response from an expired check cannot renew emulation");
        }
        ports = new FakePorts();
        using var owner = Publisher(ports, new FakeClock());
        using var competitor = Publisher(ports, new FakeClock());
        owner.Publish(Url); await owner.WhenPreparedAsync(); competitor.Publish(Url);
        await Task.Delay(40); competitor.DisposeAndFlush();
        Check(ports.Opened.Count == 1, "a canceled competing scene cannot open the owned device");
        owner.Clear(); await Idle(owner);
        Check(ports.Active == 0 && ports.Maximum == 1, "application arbitration preserves one device owner");
    }
    static async Task Policy()
    {
        var oldEnable = Environment.GetEnvironmentVariable("MAJ_PN532_NFC");
        Environment.SetEnvironmentVariable("MAJ_PN532_NFC", null);
        Check(QrNfcPublisher.CreateFromEnvironment() == null, "default-off mode creates no device client");
        Environment.SetEnvironmentVariable("MAJ_PN532_NFC", oldEnable);
        Check(Pn532NativeCdc.IsPortName("COM7") && Pn532NativeCdc.IsPortName("COM123") &&
            !Pn532NativeCdc.IsPortName("COM0") && !Pn532NativeCdc.IsPortName("COM01") &&
            !Pn532NativeCdc.IsPortName("COM7\\device") && !Pn532NativeCdc.IsPortName("/dev/ttyUSB0"), "CDC configuration accepts only explicit Windows COM names");
        var ports = new FakePorts(); using var publisher = Publisher(ports, new FakeClock());
        var invalid = new[] { Url.Replace("https:", "http:"), Url.Replace("login.example", "other.example"),
            Url.Replace("login.example", "login.example.evil"), Url.Replace("login.example", "user@login.example"),
            Url + "#fragment", Url + "&next=evil", Url + "%0A", Url + "\n", Url.Replace("authorize", "other"),
            Url.Replace("login.example", "login.example:444"), Url.Replace("fake-1", ""), Url + new string('x', 1100) };
        foreach (var url in invalid) publisher.Publish(url);
        await Idle(publisher);
        Check(ports.Opened.Count == 0, "foreign domains, malformed and oversized login URLs never open a port");
    }
    public static async Task Main()
    {
        Type4(); await Policy(); await Transport(); await Failures();
        await LoginLifecycle.Run(Check);
        Console.WriteLine($"{_passed} checks passed; mock devices and server only.");
    }
}
