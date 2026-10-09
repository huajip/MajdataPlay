// Narrow test doubles compile and drive production LoginManager control flow.
// They do not simulate Unity rendering, actual server authorization or hardware.
using System.Net;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace Cysharp.Threading.Tasks
{
    [AsyncMethodBuilder(typeof(UniTaskBuilder))]
    public readonly struct UniTask(Task task)
    {
        public TaskAwaiter GetAwaiter() => (task ?? Task.CompletedTask).GetAwaiter();
        public static Task SwitchToThreadPool() => Task.CompletedTask;
        public static Task Yield() => Task.Delay(1);
        public static Task Delay(int milliseconds) => Task.CompletedTask;
    }
    public struct UniTaskBuilder
    {
        private AsyncTaskMethodBuilder builder;
        public static UniTaskBuilder Create() => new() { builder = AsyncTaskMethodBuilder.Create() };
        public UniTask Task => new(builder.Task);
        public void SetResult() => builder.SetResult();
        public void SetException(Exception e) => builder.SetException(e);
        public void SetStateMachine(IAsyncStateMachine machine) => builder.SetStateMachine(machine);
        public void Start<T>(ref T machine) where T : IAsyncStateMachine => builder.Start(ref machine);
        public void AwaitOnCompleted<TA,TS>(ref TA awaiter,ref TS machine) where TA:INotifyCompletion where TS:IAsyncStateMachine => builder.AwaitOnCompleted(ref awaiter,ref machine);
        public void AwaitUnsafeOnCompleted<TA,TS>(ref TA awaiter,ref TS machine) where TA:ICriticalNotifyCompletion where TS:IAsyncStateMachine => builder.AwaitUnsafeOnCompleted(ref awaiter,ref machine);
    }
    [AsyncMethodBuilder(typeof(UniTaskVoidBuilder))]
    public readonly struct UniTaskVoid(Task task)
    {
        public Task AsTask() => task;
        public void Forget() { }
    }
    public struct UniTaskVoidBuilder
    {
        private AsyncTaskMethodBuilder builder;
        public static UniTaskVoidBuilder Create() => new() { builder = AsyncTaskMethodBuilder.Create() };
        public UniTaskVoid Task => new(builder.Task);
        public void SetResult() => builder.SetResult();
        public void SetException(Exception e) => builder.SetException(e);
        public void SetStateMachine(IAsyncStateMachine machine) => builder.SetStateMachine(machine);
        public void Start<T>(ref T machine) where T : IAsyncStateMachine => builder.Start(ref machine);
        public void AwaitOnCompleted<TA,TS>(ref TA awaiter,ref TS machine) where TA:INotifyCompletion where TS:IAsyncStateMachine => builder.AwaitOnCompleted(ref awaiter,ref machine);
        public void AwaitUnsafeOnCompleted<TA,TS>(ref TA awaiter,ref TS machine) where TA:ICriticalNotifyCompletion where TS:IAsyncStateMachine => builder.AwaitUnsafeOnCompleted(ref awaiter,ref machine);
    }
}
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    public class MonoBehaviour { }
    public class GameObject
    {
        public bool Active;
        public void SetActive(bool active) => Active=active;
        public T GetComponent<T>() where T:new() => new();
    }
    public struct Color
    {
        public Color(float r,float g,float b) { }
        public static Color white => new(1,1,1);
        public static Color red => new(1,0,0);
    }
    public class Texture2D { public Texture2D(int width,int height) { } public void LoadImage(byte[] png) { } }
    public class Sprite { }
}
namespace QRCoder
{
    public sealed class QRCodeGenerator
    {
        public enum ECCLevel { Q }
        public object CreateQrCode(string location, ECCLevel level) => new object();
    }
    public sealed class PngByteQRCode
    {
        public PngByteQRCode(object data) { }
        public byte[] GetGraphic(int size, bool quietZones) => Array.Empty<byte>();
    }
}
namespace UnityEngine.UI
{
    public class RawImage { public UnityEngine.Texture2D texture; public UnityEngine.Color color; }
}
namespace TMPro
{
    public class TextMeshProUGUI { public string text; public UnityEngine.Color color; }
    public class TMP_InputField
    {
        public sealed class Changed { public void AddListener(Action<string> action) { } }
        public readonly Changed onValueChanged=new();
        public string text="";
        public bool readOnly;
        public void Select() { }
        public void ActivateInputField() { }
        public void DeactivateInputField() { }
        public void MoveTextEnd(bool value) { }
    }
}
namespace MajdataPlay.Buffers
{
    public class PooledList<T> : List<T>, IDisposable { public void Dispose() { } }
}
namespace MajdataPlay.Diagnostics
{
    public static class MajDebug
    {
        public static void LogException(Exception exception) { }
        public static void LogError(object text) { }
        public static void LogDebug(object text) { }
        public static void LogInfo(object text) { }
    }
}
namespace MajdataPlay.IO
{
    public enum SensorArea { A3,A4,A5,A6,B2,B3,B4,B6,C,D3,E3,E4,E7 }
    public enum ButtonZone { A4,A5 }
    public static class InputManager
    {
        public static int Refresh, Cancel, PlainLogin;
        public static bool IsSensorPressedInThisFrame(SensorArea area) => area==SensorArea.A6 && Interlocked.Exchange(ref Refresh,0)==1;
        public static bool IsSensorClickCompletedInThisFrame(SensorArea area) =>
            area==SensorArea.A5 ? Interlocked.Exchange(ref Cancel,0)==1 : area==SensorArea.A4 && Interlocked.Exchange(ref PlainLogin,0)==1;
        public static bool IsButtonClickCompletedInThisFrame(ButtonZone area) => false;
    }
}
namespace MajdataPlay.Utils
{
    public static class TextExtensions { public static string i18n(this string value,params object[] args) => value; }
}
namespace MajdataPlay.Net
{
    public enum NetAuthMethodOption { Plain,QRCode }
    public enum HttpErrorCode { Timeout,InvalidRequest,Unreachable,Unsuccessful }
    public struct UserSummary { public string Username {get;set;} }
    public struct MajNetAccountSongScore { }
    public class RuntimeConfig
    {
        public string AuthUsername,AuthPassword,Username;
        public NetAuthMethodOption AuthMethod;
        public UnityEngine.Sprite Avatar;
    }
    public class ApiEndpoint
    {
        public string Name="Mock";
        public Uri Url=new("https://login.example/api/");
        public RuntimeConfig RuntimeConfig=new();
        public bool AutoLogin;
        public string Username="",Password="";
    }
    public class EndpointResponse
    {
        public bool IsSuccessfully=true,IsDeserializable=true;
        public HttpStatusCode StatusCode;
        public HttpErrorCode ErrorCode;
        public string Message="",Json="";
        public Dictionary<string,IEnumerable<string>> Headers=new();
        public bool TryDeserialize<T>(out T value,out Exception exception)
        { value=JsonConvert.DeserializeObject<T>(Json); exception=null; return true; }
    }
    public class UserResponse
    {
        public bool TryDeserialize(out UserSummary? summary,out Exception exception)
        { summary=null; exception=null; return true; }
    }
    public static class Online
    {
        public static int Requests,Revokes,Checks;
        public static HttpStatusCode CheckStatus=HttpStatusCode.Accepted;
        public static TaskCompletionSource<bool> DelayRequest;
        public static TaskCompletionSource<HttpStatusCode> DelayCheck;
        public static bool IsMachineRegistered(ApiEndpoint endpoint)=>true;
        public static ValueTask<EndpointResponse> RegisterAsync(ApiEndpoint endpoint,object machine,CancellationToken token)=>
            ValueTask.FromResult(new EndpointResponse());
        public static async ValueTask<EndpointResponse> AuthRequestAsync(ApiEndpoint endpoint,CancellationToken token)
        {
            var id=Interlocked.Increment(ref Requests);
            if(DelayRequest!=null) await DelayRequest.Task;
            return new EndpointResponse {StatusCode=HttpStatusCode.Created,Json="{\"RequestId\":\"fake-"+id+"\"}",
                Headers=new(){["location"]=new[]{"https://login.example/machine/authorize?auth-id=fake-"+id}}};
        }
        public static async ValueTask<EndpointResponse> AuthCheckAsync(ApiEndpoint endpoint,string id,CancellationToken token)
        {
            Interlocked.Increment(ref Checks);
            var delayed = Interlocked.Exchange(ref DelayCheck, null);
            var status = delayed == null ? CheckStatus : await delayed.Task;
            return new EndpointResponse {StatusCode=status};
        }
        public static ValueTask<EndpointResponse> AuthRevokeAsync(ApiEndpoint endpoint,string id)
        { Interlocked.Increment(ref Revokes);return ValueTask.FromResult(new EndpointResponse()); }
        public static ValueTask<EndpointResponse> LoginAsync(ApiEndpoint endpoint,string username,string password)=>
            ValueTask.FromResult(new EndpointResponse {StatusCode=HttpStatusCode.OK});
        public static ValueTask<UserResponse> GetUserInfoAsync(ApiEndpoint endpoint,CancellationToken token)=>ValueTask.FromResult(new UserResponse());
        public static ValueTask<MajNetAccountSongScore[]> GetUserScoresAsync(ApiEndpoint endpoint,CancellationToken token)=>ValueTask.FromResult(Array.Empty<MajNetAccountSongScore>());
        public static ValueTask<UnityEngine.Sprite> GetUserIconAsync(ApiEndpoint endpoint,string name,Action<string,bool> onError)=>ValueTask.FromResult<UnityEngine.Sprite>(null);
    }
}
namespace MajdataPlay
{
    public enum MajScenes { Title,List }
    public static class MajEnv
    {
        public static Net.ApiEndpoint[] ApiEndpoints=[];
        public static object MachineInfo=new();
    }
    public static class MajTimeline { public static double UnscaledDeltaTime=>1; }
    public static class ScoreManager { public static void LoadOnlineScores(Net.MajNetAccountSongScore[] scores) { } }
    public static class MajInstances { public static SceneSwitcher SceneSwitcher=new(); }
    public class SceneSwitcher
    {
        public static MajScenes LastScene=MajScenes.Title;
        public Task FadeOutAsync()=>Task.CompletedTask;
        public Task FadeInAsync()=>Task.CompletedTask;
        public void SwitchScene(string name,bool value) { }
        public void SetLoadingText(string text) { }
        public void SetLoadingText(string text,UnityEngine.Color color) { }
    }
    public static class SongStorage
    {
        public static Task RefreshAsync(IProgress<string> progress)=>Task.CompletedTask;
        public static Task<bool> RefreshUserOnlineFavAsync(IProgress<string> progress)=>Task.FromResult(true);
    }
}
