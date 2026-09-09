#!/usr/bin/env python3
"""Deleted-account regression against the isolated setup-test.py server only."""
import argparse
import json
import secrets
import urllib.request
import uuid
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--credentials', required=True)
p.add_argument('--report', required=True)
a = p.parse_args()
creds = json.loads(Path(a.credentials).read_text())
base = 'http://127.0.0.1:18096'
headers = {'Content-Type': 'application/json', 'Authorization': 'MediaBrowser Client="KidGuardTests", Device="IsolatedTest", DeviceId="kidguard-tests", Version="0.1", Token="' + creds['adminToken'] + '"'}
def call(path, data=None, method=None):
    req = urllib.request.Request(base + path, data=None if data is None else json.dumps(data).encode(), headers=headers, method=method)
    with urllib.request.urlopen(req, timeout=60) as response:
        body = response.read()
        return json.loads(body) if body else None

assert call('/System/Info/Public')['Id'] == creds['serverId'], 'Refusing a different server'
items = call('/Items?recursive=true&includeItemTypes=Movie')['Items']
assert any(i['Name'] == 'KG Gentle' for i in items), 'Synthetic fixture library required'
before = {p['Id'] for p in call('/KidGuard/Dashboard')['Profiles']}
draft = call('/KidGuard/Profiles', {'Id': str(uuid.uuid4()), 'Label': 'Uncreated deletion-test draft', 'NewUsername': 'kg-delete-draft-' + secrets.token_hex(4), 'Age': 9})
created = []
for _ in range(2):
    profile = call('/KidGuard/Profiles', {'Id': str(uuid.uuid4()), 'Label': 'Disposable deletion test', 'NewUsername': 'kg-delete-' + secrets.token_hex(4), 'Age': 9})
    call('/KidGuard/Profiles/' + profile['Id'] + '/Apply', {'Revision': profile['Revision'], 'Confirm': True, 'NewPassword': secrets.token_urlsafe(20)})
    created.append(call('/KidGuard/Profiles/' + profile['Id']))
for profile in created:
    call('/Users/' + profile['UserId'], method='DELETE')
after = call('/KidGuard/Dashboard')
remaining = {p['Id'] for p in after['Profiles']}
results = []
def check(name, passed):
    assert passed, name
    results.append({'test': name, 'result': 'PASS'})
    print('PASS', name)
check('Both deleted accounts disappear from KidGuard dashboard', all(p['Id'] not in remaining for p in created))
check('Existing profiles and uncreated draft are preserved', remaining == before | {draft['Id']})
check('No deleted account is recreated', all(p['UserId'].replace('-', '') not in {u['Id'].replace('-', '') for u in call('/Users')} for p in created))
check('Deleted profile tags are removed from media', not any(p['ActiveTag'] in item.get('Tags', []) for item in call('/Items?recursive=true&includeItemTypes=Movie,Series,Season,Episode&fields=Tags')['Items'] for p in created))
check('Deletion recorded in audit', all(any(e['ProfileId'] == p['Id'] and 'user was deleted' in e['Action'] for e in after['Audit']) for p in created))
check('Repeated dashboard request is stable', {p['Id'] for p in call('/KidGuard/Dashboard')['Profiles']} == remaining)
Path(a.report).write_text(json.dumps({'server': '12.0.0', 'pluginVersion': '0.1.2.0', 'tests': results}, indent=2) + '\n')
