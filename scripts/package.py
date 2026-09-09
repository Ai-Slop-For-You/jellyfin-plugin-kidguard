#!/usr/bin/env python3
"""Build, test and package; never installs or publishes."""
import argparse, hashlib, json, os, pathlib, subprocess, urllib.parse, zipfile
root=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--out',type=pathlib.Path,default=root/'artifacts');p.add_argument('--base-url');a=p.parse_args()
if a.base_url:
 u=urllib.parse.urlparse(a.base_url)
 if u.scheme!='https' or not u.netloc or u.username or u.password or u.query or u.fragment: p.error('--base-url must be an HTTPS directory URL without credentials, query or fragment')
out=a.out.resolve();out.mkdir(parents=True,exist_ok=True);dotnet=os.environ.get('DOTNET','dotnet')
subprocess.run([dotnet,'build','KidGuard.sln','-c','Release','-p:RestoreLockedMode=true','-m:1'],cwd=root,check=True)
subprocess.run([dotnet,'test','KidGuard.sln','-c','Release','--no-build','--no-restore','-m:1','--logger','trx;LogFileName=unit-tests.trx','--results-directory',str(out)],cwd=root,check=True)
version='0.1.2.0';timestamp='2026-09-09T00:00:00Z';name='KidGuard_'+version+'.zip'
meta={'guid':'f2247450-a459-4c15-9ee2-9e56c8737ce1','name':'KidGuard','version':version,'targetAbi':'12.0.0.0','framework':'net10.0','owner':'KidGuard contributors','category':'General','overview':'Parent-reviewed individual child libraries','description':'Evaluated preview: individual child profiles, transparent recommendations, native policies and exact approved snapshots. Read the security scope before use.','changelog':'Remove orphaned profiles after Jellyfin user deletion, with durable metadata cleanup and recovery.','timestamp':timestamp,'status':'Active','autoUpdate':False,'assemblies':['Jellyfin.Plugin.KidGuard.dll','KidGuard.Core.dll']}
binary=root/'src/Jellyfin.Plugin.KidGuard/bin/Release/net10.0'
files={dll:(binary/dll).read_bytes() for dll in meta['assemblies']}
files.update({'meta.json':json.dumps(meta,indent=2).encode(),'README.md':(root/'README.md').read_bytes(),'LICENSE':(root/'LICENSE').read_bytes(),'SECURITY.md':(root/'docs/SECURITY.md').read_bytes(),'INSTALLATION.md':(root/'docs/INSTALLATION.md').read_bytes()})
def writezip(path,entries):
 with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
  for key,value in sorted(entries.items()):
   info=zipfile.ZipInfo(key,(2026,9,8,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o100644<<16;z.writestr(info,value)
writezip(out/name,files)
base=(a.base_url or 'https://example.invalid/kidguard/').rstrip('/')+'/'
entry={k:meta[k] for k in ['guid','name','overview','description','owner','category']}
entry['versions']=[{'version':version,'targetAbi':meta['targetAbi'],'changelog':meta['changelog'],'timestamp':timestamp,'sourceUrl':base+name,'checksum':hashlib.md5((out/name).read_bytes()).hexdigest()}]
(out/('repository.json' if a.base_url else 'repository.template.json')).write_text(json.dumps([entry],indent=2)+'\n')
source={}
for f in root.rglob('*'):
 if not f.is_file():continue
 rel=f.relative_to(root)
 if any(part in {'.git','bin','obj','.dev','artifacts','__pycache__'} for part in rel.parts) or f.suffix=='.pyc' or out in f.parents:continue
 source['KidGuard/'+rel.as_posix()]=f.read_bytes()
source_name='KidGuard_'+version+'_source.zip';writezip(out/source_name,source)
(out/'SHA256SUMS').write_text(''.join(hashlib.sha256((out/n).read_bytes()).hexdigest()+'  '+n+'\n' for n in [name,source_name]))
print('Installable release:',out/name);print('Corresponding source:',out/source_name);print('Repository metadata is a template.' if not a.base_url else 'Repository metadata generated; hosting remains your deployment step.')
