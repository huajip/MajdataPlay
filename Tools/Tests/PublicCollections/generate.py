"""Compile the exact changed production methods with offline Unity test doubles."""
from pathlib import Path
root=Path(__file__).resolve().parents[3]
sources={
 'SongStorage': (root/'Assets/Scripts/Misc/Base/SongStorage.cs', ['RefreshUserOnlineFavAsync','GetCollections','AddAllFolderAndDans','IsKnownPublicCollection','AddOnlineCollectionIfMissing','AppendPublicOnlineCollectionsAsync','GetLocalDanCollectionAsync','GetOnlineDanCollectionAsync','GetDanCollectionAsync']),
 'Online': (root/'Assets/Scripts/Misc/Net/Online.cs',['GetPublicChartCollectionsAsync','GetUserOnlineFavCollection'])}
def extract(text,name):
 import re
 match=re.search(r'^        (?:internal|private|public|static).*?\b'+name+r'\(',text,re.M)
 assert match, name
 start=match.start(); pos=text.index(')',match.end())+1
 # expression-bodied helper is kept verbatim.
 semi=text.find(';',pos); brace=text.find('{',pos)
 if '=>' in text[pos:semi] and (brace<0 or semi<brace): return text[start:semi+1]
 depth=0; i=brace; quoted=False; escaped=False
 while i<len(text):
  c=text[i]
  if quoted:
   if escaped: escaped=False
   elif c=='\\': escaped=True
   elif c=='"': quoted=False
  elif c=='"': quoted=True
  elif c=='{': depth+=1
  elif c=='}':
   depth-=1
   if depth==0:return text[start:i+1]
  i+=1
 raise AssertionError(name)
header='using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Net;using MajdataPlay;using MajdataPlay.Net;using MajdataPlay.Collections;using MajdataPlay.Utils;using Cysharp.Text;using Cysharp.Threading.Tasks;\n'
out=root/'Temp/PublicCollections';out.mkdir(parents=True,exist_ok=True)
for cls,(path,names) in sources.items():
 body='\n'.join(extract(path.read_text(),name) for name in names)
 ns='MajdataPlay.Net' if cls=='Online' else 'MajdataPlay'
 (out/(cls+'.cs')).write_text(header+f'namespace {ns}{{internal static partial class {cls}{{\n'+body+'\n}}')
print('Extracted exact production methods:',sum(len(x[1]) for x in sources.values()))
