#!/usr/bin/env python3
"""Additional actual-server route probes against the fixture credentials from integration.py."""
import argparse,json,pathlib,urllib.request,urllib.error
p=argparse.ArgumentParser();p.add_argument('--credentials',required=True);p.add_argument('--report',required=True);args=p.parse_args();c=json.load(open(args.credentials));results=[]
def request(path,data=None,child=True,expected=(200,204)):
 token=c['childToken'] if child else c['adminToken'];h={'Content-Type':'application/json','Authorization':'MediaBrowser Client="KidGuardTests", Device="Fixture", DeviceId="kidguard-tests", Version="0.1", Token="'+token+'"'}
 try:
  with urllib.request.urlopen(urllib.request.Request('http://127.0.0.1:18096'+path,data=None if data is None else json.dumps(data).encode(),headers=h),timeout=30) as r:code=r.status;body=r.read()
 except urllib.error.HTTPError as e:code=e.code;body=e.read()
 assert code in expected,(path,code,body[:250]);return json.loads(body) if body and body[:1] in [b'{',b'['] else None
assert request('/System/Info/Public',child=False)['Id']==c['serverId']
request('/Videos/'+c['gentleId']+'/master.m3u8?mediaSourceId='+c['gentleId'])
results.append({'test':'Approved HLS master positive control','result':'PASS'})
for path,label in [('/Videos/'+c['blockedId']+'/master.m3u8?mediaSourceId='+c['blockedId'],'Blocked HLS master'),('/Videos/'+c['blockedId']+'/main.m3u8','Blocked HLS variant'),('/Videos/'+c['blockedId']+'/hls1/main/0.ts?runtimeTicks=20000000&actualSegmentLengthTicks=20000000','Blocked HLS segment'),('/Items/'+c['blockedId']+'/File','Blocked original file'),('/Items/'+c['gentleId']+'/PlaybackInfo?mediaSourceId='+c['blockedId'],'Cross-item media source')]:
 request(path,expected=(401,403,404));results.append({'test':label,'result':'PASS'})
collection=request('/Collections?name=KG-Safety-Collection&ids='+c['gentleId']+','+c['blockedId'],{},False)
for path,label in [('/Items?parentId='+collection['Id']+'&recursive=true','Collection browsing')]:
 data=request(path,expected=(200,401,403,404));assert c['blockedId'] not in json.dumps(data);results.append({'test':label,'result':'PASS'})
playlist=request('/Playlists',{'Name':'KG-Safety-Playlist','Ids':[c['gentleId'],c['blockedId']],'UserId':c['adminId'],'MediaType':'Video','IsPublic':True},False)
data=request('/Playlists/'+playlist['Id']+'/Items?userId='+c['childId'],expected=(200,401,403,404));assert c['blockedId'] not in json.dumps(data);results.append({'test':'Playlist browsing','result':'PASS'})
pathlib.Path(args.report).write_text(json.dumps({'server':'12.0.0','tests':results,'count':len(results)},indent=2));print('Passed',len(results),'additional route probes. Denial responses do not certify all HLS/transcoding client combinations.')
