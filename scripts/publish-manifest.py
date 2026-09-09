#!/usr/bin/env python3
"""Update the public Jellyfin manifest after release assets are published."""
import argparse
import base64
import json
import os
import re
import urllib.error
import urllib.parse
import urllib.request


def merge_manifest(existing, incoming):
    merged = {entry['guid']: entry for entry in existing}
    for entry in incoming:
        versions = {v['version']: v for v in merged.get(entry['guid'], {}).get('versions', [])}
        versions.update({v['version']: v for v in entry['versions']})
        merged[entry['guid']] = dict(entry, versions=sorted(
            versions.values(), key=lambda v: tuple(map(int, v['version'].split('.'))), reverse=True))
    return list(merged.values())


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--repository', required=True)
    p.add_argument('--branch', required=True)
    p.add_argument('--manifest', required=True)
    args = p.parse_args()
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', args.repository):
        p.error('Expected OWNER/REPOSITORY')
    headers = {'Authorization': 'Bearer ' + os.environ['GH_TOKEN'],
               'Accept': 'application/vnd.github+json', 'User-Agent': 'KidGuard-release',
               'X-GitHub-Api-Version': '2022-11-28'}
    url = 'https://api.github.com/repos/' + args.repository + '/contents/manifest.json'
    def request(target, data=None):
        payload = None if data is None else json.dumps(data).encode()
        req = urllib.request.Request(target, data=payload, headers=headers,
                                     method='GET' if data is None else 'PUT')
        with urllib.request.urlopen(req, timeout=30) as response:
            return json.load(response)
    sha = None
    existing = []
    try:
        current = request(url + '?ref=' + urllib.parse.quote(args.branch, safe=''))
        sha = current['sha']
        existing = json.loads(base64.b64decode(current['content']))
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
    with open(args.manifest) as f:
        incoming = json.load(f)
    content = json.dumps(merge_manifest(existing, incoming), indent=2) + '\n'
    data = {'message': 'Update Jellyfin plugin catalog', 'branch': args.branch,
            'content': base64.b64encode(content.encode()).decode()}
    if sha:
        data['sha'] = sha
    request(url, data)
    print('Catalog: https://raw.githubusercontent.com/' + args.repository + '/' + args.branch + '/manifest.json')


if __name__ == '__main__':
    main()
