#!/usr/bin/env python3
"""Destructive fixture tests. ONLY use the isolated server provisioned by setup-test.py."""
import argparse, json, pathlib, secrets, time, urllib.request, urllib.error, uuid
parser=argparse.ArgumentParser();parser.add_argument('--credentials',required=True);parser.add_argument('--media',required=True);parser.add_argument('--report',required=True);args=parser.parse_args()
creds=json.load(open(args.credentials));BASE='http://127.0.0.1:18096';admin=creds['adminToken'];report=[]
def call(path,data=None,token=admin,expected=(200,204),raw=False):
 headers={'Content-Type':'application/json','Authorization':'MediaBrowser Client="KidGuardTests", Device="IsolatedTest", DeviceId="kidguard-tests", Version="0.1"'+(', Token="'+token+'"' if token else '')}
 req=urllib.request.Request(BASE+path,data=None if data is None else json.dumps(data).encode(),headers=headers)
 try:
  with urllib.request.urlopen(req,timeout=60) as r: code=r.status;b=r.read()
 except urllib.error.HTTPError as e:code=e.code;b=e.read()
 assert code in expected, f'{path}: HTTP {code}: {b[:500]!r}'
 if raw:return b
 return json.loads(b) if b else None

def check(name,fn):
 fn();report.append({'test':name,'result':'PASS'});print('PASS',name,flush=True)
def scan():
 if not call('/KidGuard/Dashboard')['Progress']['Running']:call('/KidGuard/Analyze',{},expected=(202,))
 for _ in range(120):
  if not call('/KidGuard/Dashboard')['Progress']['Running']:return
  time.sleep(.2)
 raise AssertionError('Analysis timed out')
def current(p):return call('/KidGuard/Profiles/'+p['Id'])
def overrides(p,ids,kind):
 p=current(p);call('/KidGuard/Profiles/'+p['Id']+'/Overrides',{'Items':ids,'Choice':kind,'Revision':p['Revision']});return current(p)
def apply(p):
 p=current(p);call('/KidGuard/Profiles/'+p['Id']+'/Apply',{'Confirm':True,'Revision':p['Revision'],'NewPassword':password});return current(p)
def denied(path,token):call(path,token=token,expected=(401,403,404))
def includes(items,id):return any(i.get('Id','').replace('-','')==id.replace('-','') for i in items)
info=call('/System/Info/Public')
assert info['Version']=='12.0.0'
assert info['Id']==creds['serverId'], 'Refusing a different server'
scan()
items=call('/Items?recursive=true&includeItemTypes=Movie,Series,Season,Episode&fields=Tags')['Items'];byname={i['Name']:i for i in items}
assert all(n in byname for n in ['KG Gentle','KG Mature','KG Series','KG Episode 2']), 'Fixture server required'
gentle=byname['KG Gentle']['Id'];mature=byname['KG Mature']['Id'];series=byname['KG Series']['Id'];bad=byname['KG Episode 2']['Id']
suffix=secrets.token_hex(3);password=secrets.token_urlsafe(20)
p=call('/KidGuard/Profiles',{'Id':str(uuid.uuid4()),'Label':'Child 6 '+suffix,'NewUsername':'kgchild-'+suffix,'Age':6,'Approach':'Conservative'})
check('Draft does not create a Jellyfin user',lambda: (None if not current(p).get('UserId') else (_ for _ in ()).throw(AssertionError())))
check('Anonymous cannot read admin API',lambda:denied('/KidGuard/Dashboard',None))
check('Apply requires explicit approval',lambda:call('/KidGuard/Profiles/'+p['Id']+'/Apply',{'Confirm':False,'Revision':p['Revision']},expected=(400,)))
p=overrides(p,[series],'AlwaysAllow');p=overrides(p,[bad],'AlwaysBlock');p=apply(p)
auth=call('/Users/AuthenticateByName',{'Username':'kgchild-'+suffix,'Pw':password},token=None);child=auth['AccessToken'];uid=auth['User']['Id']
check('Child cannot read admin API',lambda:denied('/KidGuard/Dashboard',child))
policy=call('/Users/'+uid)['Policy']
check('Native allow tag and child permission policy installed',lambda: (None if len(policy['AllowedTags'])==1 and not policy['IsAdministrator'] and not policy['EnableLiveTvAccess'] and not policy['EnableContentDownloading'] else (_ for _ in ()).throw(AssertionError())))
check('Allowed direct item lookup succeeds',lambda:call('/Users/'+uid+'/Items/'+gentle,token=child))
check('Blocked direct item lookup denied',lambda:denied('/Users/'+uid+'/Items/'+mature,child))
check('Blocked playback info denied',lambda:denied('/Items/'+mature+'/PlaybackInfo',child))
check('Blocked direct stream denied',lambda:denied('/Videos/'+mature+'/stream?static=true',child))
check('Allowed direct stream returns video bytes',lambda: (None if len(call('/Videos/'+gentle+'/stream?static=true',token=child,expected=(200,206),raw=True))>1000 else (_ for _ in ()).throw(AssertionError())))
check('Episode exception overrides allowed series',lambda:denied('/Videos/'+bad+'/stream?static=true',child))
check('Cross-user browse is denied',lambda:denied('/Items?userId='+creds['adminId']+'&recursive=true',child))
for path,label in [('/Items?recursive=true&includeItemTypes=Movie,Episode','recursive browsing'),('/Items?recursive=true&searchTerm=KG%20Mature','search'),('/Users/'+uid+'/Items/Latest','recently added'),('/Shows/NextUp?userId='+uid,'next up'),('/Movies/Recommendations?userId='+uid,'recommendations')]:
 def verify(path=path):
  data=call(path,token=child);text=json.dumps(data);assert mature not in text and bad not in text
 check('Blocked content absent from '+label,verify)
check('Live TV API denied',lambda:denied('/LiveTv/Channels',child))
check('Download route denied',lambda:denied('/Items/'+gentle+'/Download',child))
check('Manual tag application preserves unrelated tags',lambda: (None if 'KeepMe' in call('/Users/'+creds['adminId']+'/Items/'+gentle)['Tags'] else (_ for _ in ()).throw(AssertionError())))
# New episode inherits native series tag, but is not in the published snapshot.
media=pathlib.Path(args.media);episode_number=max(int(f.stem.split('S01E')[1]) for f in (media/'tv'/'KG Series'/'Season 01').glob('*.mp4'))+1;stem=media/'tv'/'KG Series'/'Season 01'/f'KG Series S01E{episode_number:02}'
stem.with_suffix('.nfo').write_text(f'<episodedetails><title>KG New Episode</title><season>1</season><episode>{episode_number}</episode><mpaa>TV-G</mpaa><lockdata>true</lockdata></episodedetails>');stem.with_suffix('.mp4').write_bytes((media/'fixture.mp4').read_bytes())
call('/Library/Refresh',{})
new=None
for _ in range(120):
 found=call('/Items?recursive=true&includeItemTypes=Episode')['Items'];new=next((i for i in found if i['Id'] not in {old['Id'] for old in items}),None)
 if new:break
 time.sleep(.25)
assert new
check('New episode stream denied before parent review',lambda:denied('/Videos/'+new['Id']+'/stream?static=true',child))
check('New episode absent from child browse',lambda:(None if not includes(call('/Shows/'+series+'/Episodes?userId='+uid,token=child)['Items'],new['Id']) else (_ for _ in ()).throw(AssertionError())))
scan()
check('Reanalysis preserves Always Block',lambda:(None if current(p)['Overrides'][str(uuid.UUID(bad))]=='AlwaysBlock' else (_ for _ in ()).throw(AssertionError())))
check('New episode stays denied after reanalysis without apply',lambda:denied('/Videos/'+new['Id']+'/stream?static=true',child))
# Second child has its own allowlist.
p2=call('/KidGuard/Profiles',{'Id':str(uuid.uuid4()),'Label':'Child 13 '+suffix,'NewUsername':'kgolder-'+suffix,'Age':13,'Approach':'Standard'})
p2=overrides(p2,[mature],'AlwaysAllow');p2=apply(p2)
auth2=call('/Users/AuthenticateByName',{'Username':'kgolder-'+suffix,'Pw':password},token=None);child2=auth2['AccessToken']
check('Second child can access their independent override',lambda:call('/Videos/'+mature+'/stream?static=true',token=child2,expected=(200,206),raw=True))
check('First child remains blocked after second child apply',lambda:denied('/Videos/'+mature+'/stream?static=true',child))
# Stale review rejected, then revoke and undo an approved title.
p=current(p);stale=p['Revision'];p=overrides(p,[gentle],'AlwaysBlock')
check('Stale approval revision rejected',lambda:call('/KidGuard/Profiles/'+p['Id']+'/Apply',{'Confirm':True,'Revision':stale},expected=(409,)))
p=apply(p)
check('Reapply removes access',lambda:denied('/Videos/'+gentle+'/stream?static=true',child))
call('/KidGuard/Profiles/'+p['Id']+'/Undo',{'Confirm':True})
check('Undo restores the preceding approved library',lambda:call('/Videos/'+gentle+'/stream?static=true',token=child,expected=(200,206),raw=True))
check('Child cannot modify overrides',lambda:call('/KidGuard/Profiles/'+p['Id']+'/Overrides',{'Items':[mature],'Choice':'AlwaysAllow','Revision':0},token=child,expected=(401,403)))
pathlib.Path(args.report).write_text(json.dumps({'server':'12.0.0','tests':report,'count':len(report)},indent=2))
creds.update({'childToken':child,'childId':uid,'profileId':p['Id'],'otherProfileId':p2['Id'],'gentleId':gentle,'blockedId':mature,'seriesId':series,'newEpisodeId':new['Id']});pathlib.Path(args.credentials).write_text(json.dumps(creds))
print('All',len(report),'live checks passed')
