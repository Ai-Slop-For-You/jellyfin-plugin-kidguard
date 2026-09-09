#!/usr/bin/env python3
import json, urllib.request, pathlib, subprocess, time, secrets
assert pathlib.Path('.dev/.kidguard-test-server').is_file(), 'Start the marked isolated dev server first'
base='http://127.0.0.1:18096'
headers={'Content-Type':'application/json','Authorization':'MediaBrowser Client="KidGuardTests", Device="IsolatedTest", DeviceId="kidguard-tests", Version="0.1"'}
def call(path,data=None):
 req=urllib.request.Request(base+path,data=None if data is None else json.dumps(data).encode(),headers=headers)
 with urllib.request.urlopen(req,timeout=60) as r:
  b=r.read();return json.loads(b) if b else None
info=call('/System/Info/Public');assert not info['StartupWizardCompleted'],'Only a fresh isolated test server is allowed'
call('/Startup/FirstUser')
pw=secrets.token_urlsafe(20)
call('/Startup/User',{'Name':'KidGuardTestAdmin','Password':pw})
call('/Startup/Configuration',{'ServerName':'KidGuard isolated test','UICulture':'en-US','MetadataCountryCode':'US','PreferredMetadataLanguage':'en'})
call('/Startup/RemoteAccess',{'EnableRemoteAccess':False,'EnableAutomaticPortMapping':False})
call('/Startup/Complete',{})
auth=call('/Users/AuthenticateByName',{'Username':'KidGuardTestAdmin','Pw':pw})
headers['Authorization'] += ', Token="'+auth['AccessToken']+'"'
path=pathlib.Path('.dev/credentials.json');path.write_text(json.dumps({'adminToken':auth['AccessToken'],'adminId':auth['User']['Id'],'password':pw,'serverId':info['Id']}));path.chmod(0o600)
root=pathlib.Path('.dev/media').resolve()
subprocess.run(['ffmpeg','-v','error','-y','-f','lavfi','-i','color=c=blue:s=160x120:d=2','-f','lavfi','-i','anullsrc=r=44100:cl=stereo','-t','2','-c:v','mpeg4','-c:a','aac',str(root/'fixture.mp4')],check=True)
for name,rating in [('KG Gentle','G'),('KG Mature','R'),('KG Unknown',''),('KG Teen','PG-13')]:
 d=root/'movies'/name;d.mkdir(exist_ok=True)
 (d/(name+'.mp4')).write_bytes((root/'fixture.mp4').read_bytes())
 (d/(name+'.nfo')).write_text(f'<movie><title>{name}</title><mpaa>{rating}</mpaa><year>2024</year><tag>KeepMe</tag><lockdata>true</lockdata></movie>')
d=root/'tv'/'KG Series';(d/'Season 01').mkdir(parents=True,exist_ok=True)
(d/'tvshow.nfo').write_text('<tvshow><title>KG Series</title><mpaa>TV-G</mpaa><lockdata>true</lockdata></tvshow>')
for ep,rating in [(1,'TV-G'),(2,'TV-MA')]:
 stem=d/'Season 01'/f'KG Series S01E{ep:02}'
 stem.with_suffix('.mp4').write_bytes((root/'fixture.mp4').read_bytes())
 stem.with_suffix('.nfo').write_text(f'<episodedetails><title>KG Episode {ep}</title><season>1</season><episode>{ep}</episode><mpaa>{rating}</mpaa><lockdata>true</lockdata></episodedetails>')
for name,kind in [('movies','movies'),('tv','tvshows')]:
 options={'PathInfos':[{'Path':str(root/name)}],'EnableRealtimeMonitor':False,'EnableInternetProviders':False,'TypeOptions':[{'Type':t,'MetadataFetchers':[],'ImageFetchers':[]} for t in ['Movie','Series','Season','Episode']]}
 call('/Library/VirtualFolders?name=KG-'+name+'&collectionType='+kind+'&refreshLibrary=false',{'LibraryOptions':options})
call('/Library/Refresh',{})
path=pathlib.Path('.dev/credentials.json');path.write_text(json.dumps({'adminToken':auth['AccessToken'],'adminId':auth['User']['Id'],'password':pw,'serverId':info['Id']}));path.chmod(0o600)
for _ in range(60):
 items=call('/Items?recursive=true&includeItemTypes=Movie,Series,Season,Episode&fields=Tags,OfficialRating')['Items']
 if len(items)>=8:break
 time.sleep(1)
print('Isolated fixture library:',[(x['Name'],x.get('OfficialRating'),x['Type']) for x in items])
call('/KidGuard/Analyze',{})
for _ in range(60):
 dash=call('/KidGuard/Dashboard')
 if not dash['Progress']['Running']:break
 time.sleep(1)
print('KidGuard analyzed:',dash['Total'],dash['Progress'])
