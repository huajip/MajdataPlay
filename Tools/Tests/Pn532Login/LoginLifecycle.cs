using System.Net;
using System.Reflection;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO;
using MajdataPlay.Net;
using MajdataPlay.Scenes.Login;
using UnityEngine;

static class LoginLifecycle
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Invoke(object target, string name) => target.GetType().GetMethod(name, Fields).Invoke(target, null);
    sealed class RunState
    {
        public LoginManager Manager;
        public QrNfcPublisher Publisher;
        public FakePorts Ports;
        public Task Process;
        public FakeClock Clock;
    }
    static async Task<RunState> Start(bool delay = false)
    {
        Online.Requests = Online.Revokes = Online.Checks = 0;
        Online.CheckStatus = HttpStatusCode.Accepted;
        Online.DelayCheck = null;
        Online.DelayRequest = delay ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        InputManager.Refresh = InputManager.Cancel = InputManager.PlainLogin = 0;
        var state = new RunState { Ports = new FakePorts(), Clock = new FakeClock(), Manager = new LoginManager() };
        state.Publisher = Tests.Publisher(state.Ports, state.Clock);
        foreach (var field in typeof(LoginManager).GetFields(Fields))
            if (field.GetCustomAttribute<SerializeField>() != null) field.SetValue(state.Manager, Activator.CreateInstance(field.FieldType));
        typeof(LoginManager).GetField("_qrCodeRawImage", Fields).SetValue(state.Manager, new UnityEngine.UI.RawImage());
        typeof(LoginManager).GetField("_enabledEndpoints", Fields).SetValue(state.Manager, new[] { new ApiEndpoint() });
        typeof(LoginManager).GetField("_nfcPublisher", Fields).SetValue(state.Manager, state.Publisher);
        state.Process = ((UniTaskVoid)Invoke(state.Manager, "LoginProcessor")).AsTask();
        if (!delay)
        {
            await Tests.Until(() => state.Ports.Opened.Count != 0);
            if (!await state.Publisher.WhenPreparedAsync()) throw new Exception("login_target_not_prepared");
        }
        return state;
    }
    static async Task Stop(RunState state)
    {
        InputManager.Cancel = 1;
        await state.Process.WaitAsync(TimeSpan.FromSeconds(3));
        state.Publisher.DisposeAndFlush();
        await state.Publisher.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
    }
    public static async Task Run(Action<bool, string> check)
    {
        var manual = await Start();
        check(Online.Requests == 1 && manual.Ports.Active == 1, "LoginManager publishes server Location after creating the QR texture");
        var old = manual.Ports.Last;
        InputManager.Refresh = 1;
        await Tests.Until(() => Online.Requests == 2 && manual.Ports.Opened.Count == 2);
        await manual.Publisher.WhenPreparedAsync();
        check(Online.Revokes == 1 && old.Closed && manual.Ports.Maximum == 1, "LoginManager refresh clears before revoking and displaying the next QR");
        await Stop(manual);
        check(manual.Ports.Active == 0 && Online.Revokes == 2, "LoginManager cancel revokes the QR and releases NFC");

        var stale = await Start();
        var delayedCheck = Online.DelayCheck = new TaskCompletionSource<HttpStatusCode>(TaskCreationOptions.RunContinuationsAsynchronously);
        var beforeCheck = Online.Checks;
        await Tests.Until(() => Online.Checks > beforeCheck);
        InputManager.Refresh = 1;
        await Tests.Until(() => Online.Requests == 2 && stale.Ports.Opened.Count == 2);
        await stale.Publisher.WhenPreparedAsync();
        Online.DelayCheck = null;
        delayedCheck.SetResult(HttpStatusCode.OK);
        await Task.Delay(30);
        check(!stale.Process.IsCompleted && stale.Ports.Active == 1 && Online.Requests == 2,
            "LoginManager discards authorization success from a QR replaced during an in-flight check");
        await Stop(stale);

        var pending = await Start();
        for (int i = 0; i < 40; i++)
        {
            pending.Clock.Advance(2);
            var before = Online.Checks;
            await Tests.Until(() => Online.Checks > before);
            await Task.Delay(5);
            Invoke(pending.Manager, "Update");
        }
        check(pending.Ports.Active == 1 && pending.Ports.Opened.Count == 1 && Online.Requests == 1,
            "LoginManager 202 responses renew the same QR beyond sixty seconds");
        await Stop(pending);

        var expired = await Start(); old = expired.Ports.Last;
        Online.CheckStatus = HttpStatusCode.NotFound;
        await Tests.Until(() => Online.Requests >= 2);
        Online.CheckStatus = HttpStatusCode.Accepted;
        await Tests.Until(() => expired.Ports.Opened.Count >= 2);
        await expired.Publisher.WhenPreparedAsync();
        check(old.Closed && Online.Revokes >= 1, "LoginManager server expiry clears before requesting a replacement");
        await Stop(expired);

        var approved = await Start(); Online.CheckStatus = HttpStatusCode.OK;
        await approved.Process.WaitAsync(TimeSpan.FromSeconds(3));
        await approved.Publisher.WhenIdleAsync();
        check(approved.Ports.Active == 0, "LoginManager normal authorization success releases NFC");
        approved.Publisher.DisposeAndFlush();

        var plain = await Start(); InputManager.PlainLogin = 1;
        await plain.Process.WaitAsync(TimeSpan.FromSeconds(3));
        await plain.Publisher.WhenIdleAsync();
        check(plain.Ports.Active == 0 && Online.Revokes == 1, "LoginManager password login success revokes the QR and releases NFC");
        plain.Publisher.DisposeAndFlush();

        var outage = await Start(); Online.CheckStatus = HttpStatusCode.ServiceUnavailable;
        outage.Clock.Advance(9);
        await outage.Publisher.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        check(outage.Ports.Active == 0, "LoginManager non-202 errors cannot keep expired emulation alive");
        await Stop(outage);

        foreach (var callback in new[] { "OnDisable", "OnDestroy", "OnApplicationQuit", "EnterList" })
        {
            var teardown = await Start(); Invoke(teardown.Manager, callback);
            await teardown.Publisher.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
            check(teardown.Ports.Active == 0, "LoginManager " + callback + " releases NFC");
            await Stop(teardown);
        }
        var late = await Start(true);
        Invoke(late.Manager, "OnDisable"); Online.DelayRequest.SetResult(true);
        await Task.Delay(30);
        check(late.Ports.Opened.Count == 0, "LoginManager late authorization response after leaving the page cannot reopen NFC");
        await Stop(late);
    }
}
