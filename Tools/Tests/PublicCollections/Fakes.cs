using System.Collections;
using System.Net;
using System.Security.Cryptography;
using Newtonsoft.Json;
using MajdataPlay.Collections;
using MajdataPlay.Net;
namespace Cysharp.Threading.Tasks { static class UniTask { public static Task SwitchToThreadPool()=>Task.CompletedTask; } }
namespace Cysharp.Text { static class ZString { public static string Format(string s,object x)=>s; } }
namespace MajdataPlay {
 enum ChartStorageType{Folder,PlayList,Dan}
 interface ISongDetail{string Hash{get;} bool IsOnline{get;}}
 class OnlineSongDetail:ISongDetail{public string Hash{get;init;} public bool IsOnline=>true; public ApiEndpoint ServerInfo{get;init;}}
 class LocalSong:ISongDetail{public string Hash{get;init;} public bool IsOnline=>false;}
 class DanInfo{public string Name{get;init;} public string Description{get;init;} public string[] SongHashs{get;init;} public bool IsPlayList{get;init;} public bool IsForceGameover{get;init;}}
 struct MajNetOnlineDanInfo {public string Id{get;init;}public string Name{get;init;}public string CreatedBy{get;init;}public string Description{get;init;}public string[] SongHashs{get;init;}public bool IsPlayList{get;init;}public bool IsForceGameover{get;init;}}
 static class TextHelpers{public static string i18n(this string s)=>s;public static Uri Combine(this Uri u,string s)=>new Uri(u.ToString().TrimEnd('/')+"/"+s);}
 static class MajDebug{public static void LogError(object x){} public static void LogException(object x){}public static void LogInfo(object x){} public static void LogDebug(object x){}}
 static class MajEnv{public const int HTTP_REQUEST_MAX_RETRY=2;public static ApiEndpoint[] ApiEndpoints=Array.Empty<ApiEndpoint>();public static SettingsModel Settings=new();}
 class SettingsModel{public OnlineSettings Online=new();} class OnlineSettings{public bool Enable=true;}
 static class Serializer {public static class Json{public static Task<(bool, T, Exception)> TryDeserializeAsync<T>(Stream s)=>Task.FromResult((true,JsonConvert.DeserializeObject<T>(new StreamReader(s).ReadToEnd()),(Exception)null));}}
 internal static partial class SongStorage {
  public static SongCollection[] Collections=Array.Empty<SongCollection>();
  static readonly List<ISongDetail> _allCharts=new();static readonly HashSet<string> _storageFav=new();static readonly Dictionary<ApiEndpoint,HashSet<Guid>> _publicOnlineCollectionIds=new();
  static DanInfo _userFavorites;static MyFavoriteSongCollection _myFavorite;const string MY_FAVORITE_FILENAME="MyFavorites.json";
  static Guid GetGuidFromStream(Stream s)=>Guid.NewGuid();
  static Task<List<SongCollection>> GetLocalCollections(string path,IProgress<string> progress)=>Task.FromResult(new List<SongCollection>());
  static Task<SongCollection> GetOnlineCollection(ApiEndpoint api,IProgress<string> progress)=>Task.FromResult(api==Fixture.Official?new OnlineSongCollection(api,api.Name,Fixture.Songs):SongCollection.Empty(api.Name));
  public static async Task Initialize(string path){_allCharts.Clear();_publicOnlineCollectionIds.Clear();_storageFav.Clear();Collections=await GetCollections(path,null);}
  public static int PublicCount=>Collections.Count(IsKnownPublicCollection);
  public static SongCollection[] PublicFolders=>Collections.Where(IsKnownPublicCollection).ToArray();
  public static void SeedCollidingForeign(ApiEndpoint api)=>_allCharts.Insert(0,new OnlineSongDetail{Hash=Fixture.Songs[0].Hash,ServerInfo=api});
 }
}
namespace MajdataPlay.Utils{static class ObjectIdHelper{public static Guid ToGuid(string s)=>new Guid(MD5.HashData(System.Text.Encoding.UTF8.GetBytes(s)));}}
namespace MajdataPlay.Collections{
 class SongCollection:IEnumerable<MajdataPlay.ISongDetail>{public string Name;public Guid Id{get;set;}public bool IsOnline{get;set;}public MajdataPlay.ChartStorageType Type{get;set;}public MajdataPlay.DanInfo DanInfo{get;set;}public MajdataPlay.ISongDetail[] Songs;public int Count=>Songs.Length;public bool IsEmpty=>Count==0;
 public SongCollection(string n,MajdataPlay.ISongDetail[] songs){Name=n;Songs=songs;}public static SongCollection Empty(string n)=>new(n,Array.Empty<MajdataPlay.ISongDetail>());public IEnumerator<MajdataPlay.ISongDetail> GetEnumerator()=>((IEnumerable<MajdataPlay.ISongDetail>)Songs).GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();}
 class OnlineSongCollection:SongCollection{public ApiEndpoint Source;public OnlineSongCollection(ApiEndpoint a,string n,MajdataPlay.ISongDetail[] s):base(n,s){Source=a;IsOnline=true;}}
 class MyFavoriteSongCollection:SongCollection{public MyFavoriteSongCollection(IEnumerable<MajdataPlay.ISongDetail> songs,HashSet<string> hashes):base("MyFavorites",songs.ToArray()){} }
}
namespace MajdataPlay.Net{
 class ApiEndpoint{public string Name;public Uri Url;public RuntimeConfigModel RuntimeConfig=new();}
 class RuntimeConfigModel{public bool IsLoggedIn;}
 enum HttpErrorCode{None,Canceled}
 class EndpointResponse {public HttpStatusCode StatusCode;public HttpErrorCode ErrorCode;public string Body;public bool IsSuccessfully=>StatusCode==HttpStatusCode.OK; public bool TryDeserialize<T>(out T rows,out Exception e){try{rows=JsonConvert.DeserializeObject<T>(Body);e=null;return true;}catch(Exception x){rows=default;e=x;return false;}}}
 class ApiStatistic{public ApiEndpoint Endpoint;public bool IsUserLoggedIn{get=>Endpoint.RuntimeConfig.IsLoggedIn;set=>Endpoint.RuntimeConfig.IsLoggedIn=value;}public ValueTask<IDisposable> LockAsync(CancellationToken token)=>ValueTask.FromResult<IDisposable>(new EmptyDisposable());}
 class EmptyDisposable:IDisposable{public void Dispose(){}}
 internal static partial class Online{
  const string API_GET_USER_FAVCOLLECTION="account/favorite/collection/list";
  static ApiStatistic GetApiEndpointStatistic(ApiEndpoint api)=>new(){Endpoint=api};
  static Task<EndpointResponse> GetAsync(Uri url,CancellationToken token){token.ThrowIfCancellationRequested();Fixture.Requests.Add(url);return Task.FromResult(Fixture.Response(url));}
 }
}
static class Fixture {
 public static ApiEndpoint Official=new(){Name="Official",Url=new Uri("https://maicharts.sdgb.app/api/")};
 public static MajdataPlay.ISongDetail[] Songs;public static string PublicJson;public static string PrivateJson;public static List<Uri> Requests=new();public static HttpStatusCode PublicStatus=HttpStatusCode.OK;public static HttpStatusCode CapabilityStatus=HttpStatusCode.OK;public static string CapabilityJson="{\"publicCollections\":true,\"publicCollectionPath\":\"maichart/collection/list\"}";
 public static EndpointResponse Response(Uri url){
  if(url.AbsolutePath.EndsWith("maichart/capabilities"))return new(){StatusCode=url.Host==Official.Url.Host?CapabilityStatus:HttpStatusCode.NotFound,Body=CapabilityJson};
  return new(){StatusCode=url.AbsolutePath.Contains("maichart/collection")?PublicStatus:HttpStatusCode.OK,Body=url.AbsolutePath.Contains("maichart/collection")?PublicJson:PrivateJson};
 }
}

namespace UnityEngine.Scripting { [AttributeUsage(AttributeTargets.Class)] class PreserveAttribute:Attribute{} }
