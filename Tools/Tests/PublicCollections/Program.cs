using System.Net;
using Newtonsoft.Json.Linq;
using MajdataPlay;
using MajdataPlay.Net;
using MajdataPlay.Collections;
static class Program{
 static int Checks;static void Check(bool b,string message){Checks++;if(!b)throw new Exception(message);}
 static async Task Main(string[] args){
  Fixture.PublicJson=File.ReadAllText(args[0]);var originalJson=Fixture.PublicJson;var expected=JArray.Parse(originalJson);
  Fixture.Songs=JArray.Parse(File.ReadAllText(args[1])).Select(x=>(ISongDetail)new OnlineSongDetail{Hash=(string)x["hash"],ServerInfo=Fixture.Official}).ToArray();
  var privateRow=new JObject{{"id","personal-private"},{"name","Private playlist"},{"songHashs",new JArray(Fixture.Songs[0].Hash)},{"isPlayList",true}};
  var privateRows=new JArray(expected.Select(x=>x.DeepClone()));privateRows.Add(privateRow);Fixture.PrivateJson=privateRows.ToString();
  var restricted=new ApiEndpoint{Name="Other restricted",Url=new Uri("https://restricted.example/api/")};MajEnv.ApiEndpoints=new[]{Fixture.Official,restricted};
  var temp=Path.Combine(Path.GetTempPath(),"task8-public-collections-"+Guid.NewGuid());Directory.CreateDirectory(temp);
  try{
   await SongStorage.Initialize(temp);
   Check(SongStorage.PublicCount==37,"fresh signed-out scan did not load all public folders");
   Check(Fixture.Requests.Count==3&&Fixture.Requests.Any(x=>x.Host==Fixture.Official.Url.Host&&x.AbsolutePath=="/api/maichart/collection/list")&&Fixture.Requests.All(x=>!x.AbsolutePath.Contains("account/")),"initial scan requested a private or foreign endpoint");
   Check(!Fixture.Official.RuntimeConfig.IsLoggedIn&&!restricted.RuntimeConfig.IsLoggedIn,"scan changed login state");
   Check(SongStorage.PublicFolders.Select(c=>c.Name).SequenceEqual(expected.Select(x=>(string)x["name"])),"folder order changed");
   foreach(var folder in SongStorage.PublicFolders)Check(folder.Count>0,"empty folder");
   var originalIds=SongStorage.PublicFolders.Select(c=>c.Id).ToArray();
   var hashes=SongStorage.PublicFolders.SelectMany(c=>c.Songs.Select(s=>s.Hash)).ToHashSet();Check(hashes.Count==1893,"coverage lost");
   Fixture.Requests.Clear();Check(await SongStorage.RefreshUserOnlineFavAsync(),"anonymous refresh failed");
   Check(SongStorage.PublicCount==37&&SongStorage.PublicFolders.Select(x=>x.Id).SequenceEqual(originalIds),"anonymous refresh lost folders");
   Check(Fixture.Requests.Count==3&&Fixture.Requests.Where(x=>x.Host=="restricted.example").All(x=>x.AbsolutePath.EndsWith("maichart/capabilities")),"signed-out refresh queried restricted source");
   Fixture.Official.RuntimeConfig.IsLoggedIn=true;restricted.RuntimeConfig.IsLoggedIn=true;Fixture.Requests.Clear();
   Check(await SongStorage.RefreshUserOnlineFavAsync(),"signed-in refresh failed");
   Check(SongStorage.PublicCount==37,"public/favorites duplicate folders");
   Check(SongStorage.Collections.Count(c=>c is OnlineSongCollection o&&o.Source==Fixture.Official&&c.Name=="Private playlist")==1,"private favorites lost");
   Check(Fixture.Requests.Any(x=>x.Host=="restricted.example"&&x.AbsolutePath=="/api/account/favorite/collection/list"),"existing signed-in restricted favorites behavior changed");
   Check(!Fixture.Requests.Any(x=>x.Host=="restricted.example"&&x.AbsolutePath.Contains("maichart/collection")),"foreign public probe");
   Fixture.Official.RuntimeConfig.IsLoggedIn=false;restricted.RuntimeConfig.IsLoggedIn=false;Fixture.Requests.Clear();
   Check(await SongStorage.RefreshUserOnlineFavAsync(),"logout refresh failed");
   Check(SongStorage.PublicCount==37&&!SongStorage.Collections.Any(c=>c.Name=="Private playlist"),"logout hid public or retained private folders");
   Check(Fixture.Requests.Count==3&&Fixture.Requests.All(x=>!x.AbsolutePath.Contains("account/")),"logout fetched private favorites");
   SongStorage.SeedCollidingForeign(restricted);Check(await SongStorage.RefreshUserOnlineFavAsync(),"collision refresh failed");
   Check(SongStorage.PublicFolders.SelectMany(c=>c.Songs).OfType<OnlineSongDetail>().All(s=>s.ServerInfo==Fixture.Official),"public folder borrowed foreign download source");
   Fixture.Official.RuntimeConfig.IsLoggedIn=true;Fixture.PublicStatus=HttpStatusCode.Unauthorized;
   Check(!await SongStorage.RefreshUserOnlineFavAsync(),"public 401 not reported");
   Check(Fixture.Official.RuntimeConfig.IsLoggedIn&&SongStorage.PublicCount==37,"public failure logged user out or dropped previous public folders");
   Fixture.Official.RuntimeConfig.IsLoggedIn=false;Fixture.PublicStatus=HttpStatusCode.OK;Fixture.PublicJson="invalid json";
   Check(!await SongStorage.RefreshUserOnlineFavAsync()&&SongStorage.PublicCount==37,"malformed response erased public folders");
   Fixture.PublicJson=new JArray(expected.Select(x=>x.DeepClone()).Concat(new[]{privateRow.DeepClone()})).ToString();
   Check(await SongStorage.RefreshUserOnlineFavAsync()&&SongStorage.PublicCount==37&&!SongStorage.Collections.Any(c=>c.Name=="Private playlist"),"public endpoint accepted private data");
   Fixture.PublicJson="[]";Check(await SongStorage.RefreshUserOnlineFavAsync()&&SongStorage.PublicCount==0,"empty server collection result not applied");
   Fixture.PublicJson=originalJson;Check(await SongStorage.RefreshUserOnlineFavAsync()&&SongStorage.PublicCount==37,"recovery failed");
   Fixture.Requests.Clear();using(var cancel=new CancellationTokenSource()){
    cancel.Cancel();try{await Online.GetPublicChartCollectionsAsync(Fixture.Official,cancel.Token);throw new Exception("cancellation ignored");}catch(OperationCanceledException){}
   }
   Check(Fixture.Requests.Count==0,"canceled load sent request");
   Check(await Online.GetUserOnlineFavCollection(restricted)==null&&Fixture.Requests.Count==0,"private anonymous guard removed");
   var savedUrl=Fixture.Official.Url;Fixture.Official.Url=new Uri("https://another-chart-server.example:8443/charts/api/");
   Check(await SongStorage.RefreshUserOnlineFavAsync()&&SongStorage.PublicCount==37,"second declared server refused: hardcoded host policy");
   Check(Fixture.Requests.Any(x=>x.Host=="another-chart-server.example"&&x.AbsolutePath=="/charts/api/maichart/collection/list"),"capability path did not use configured API root");
   Fixture.CapabilityJson="{}";Check(await SongStorage.RefreshUserOnlineFavAsync()&&SongStorage.PublicCount==0,"missing capability broadened default access");
   Fixture.CapabilityJson="{\"publicCollections\":false,\"publicCollectionPath\":\"maichart/collection/list\"}";Check(await SongStorage.RefreshUserOnlineFavAsync()&&SongStorage.PublicCount==0,"explicit disabled capability ignored");
   Fixture.CapabilityJson="{\"publicCollections\":true,\"publicCollectionPath\":\"maichart/collection/list\"}";Check(await SongStorage.RefreshUserOnlineFavAsync()&&SongStorage.PublicCount==37,"capability recovery failed");
   Fixture.CapabilityStatus=HttpStatusCode.ServiceUnavailable;Check(!await SongStorage.RefreshUserOnlineFavAsync()&&SongStorage.PublicCount==37,"temporary capability failure removed cached public folders");Fixture.CapabilityStatus=HttpStatusCode.OK;
   foreach(var path in new[]{"https://evil.test/public","//evil.test/public","file:///tmp/data","maichart/collection/list?secret=x","maichart/collection/list#fragment","\\evil.test"}){
    Check(!PublicChartCollections.TryGetCollectionUri(Fixture.Official.Url,new(){PublicCollections=true,PublicCollectionPath=path},out _),"unsafe capability endpoint accepted");
   }
   Check(PublicChartCollections.TryGetCollectionUri(new Uri("http://local-chart-server:8080/api/"),new(){PublicCollections=true,PublicCollectionPath="maichart/collection/list"},out var local)&&local.Host=="local-chart-server","explicit per-server capability unnecessarily tied to domain/protocol");
   Check(!PublicChartCollections.TryGetCollectionUri(savedUrl,new(){PublicCollections=false,PublicCollectionPath="maichart/collection/list"},out _),"default disabled capability accepted");
   Check(!PublicChartCollections.IsApiRoot(new Uri("api/",UriKind.Relative)),"relative source accepted");
   Fixture.Official.Url=savedUrl;
   MajEnv.Settings.Online.Enable=false;Fixture.Requests.Clear();await SongStorage.Initialize(temp);
   Check(SongStorage.PublicCount==0&&Fixture.Requests.Count==0,"disabled online setting bypassed");
   Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{ChecksPassed=Checks,ProductionMethodsCompiled=11,FullUnityBuild=false,RealNetworkOrGameStarted=false}));
  }finally{Directory.Delete(temp,true);}
 }
}
