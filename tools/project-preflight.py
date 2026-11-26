#!/usr/bin/env python3
"""Check a gic checkout before launching Tuanjie; optionally pin a known-good asset tree."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent.parent
ASSET_DIRS = (Path('Assets/Mirror'), Path('Assets/AddressableAssetsData'))
REQUIRED = (
    Path('Assets/Mirror/Core/NetworkIdentity.cs'),
    Path('Assets/Mirror/Core/NetworkIdentity.cs.meta'),
    Path('Assets/Mirror/Core/NetworkClient.cs'),
    Path('Assets/AddressableAssetsData/AddressableAssetSettings.asset'),
    Path('Assets/AddressableAssetsData/AddressableAssetSettings.asset.meta'),
)
TEXT_SUFFIXES = {'.cs', '.asmdef', '.asmref', '.md', '.txt', '.json', '.asset', '.meta', '.prefab', '.unity'}


def fingerprints():
    result = {}
    for directory in ASSET_DIRS:
        files = sorted(p for p in (ROOT / directory).rglob('*') if p.is_file())
        digest = hashlib.sha256()
        for file in files:
            if file.name in ('.DS_Store', 'Thumbs.db') or file.name.endswith(('.bin', '.bin.meta')):
                continue
            data = file.read_bytes()
            if file.suffix in TEXT_SUFFIXES and b'\0' not in data:
                data = data.replace(b'\r\n', b'\n')
            digest.update(file.relative_to(ROOT).as_posix().encode('utf-8') + b'\0')
            digest.update(hashlib.sha256(data).digest())
        result[directory.as_posix()] = digest.hexdigest()
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--write-fingerprint', type=Path, help='create a fingerprint on a verified checkout')
    mode.add_argument('--check-fingerprint', type=Path, help='verify against a known-good fingerprint')
    args = parser.parse_args()
    manifest = json.loads((ROOT / 'Packages/manifest.json').read_text(encoding='utf-8'))['dependencies']
    lock = json.loads((ROOT / 'Packages/packages-lock.json').read_text(encoding='utf-8'))['dependencies']
    issues = []
    for name, version in manifest.items():
        if version.startswith('file:'):
            if not (ROOT / 'Packages' / version[5:] / 'package.json').is_file():
                issues.append(f'missing local package: {name} ({version})')
        elif lock.get(name, {}).get('version') != version:
            issues.append(f'manifest and lock disagree: {name}')
    issues.extend(f'missing asset dependency: {p.as_posix()}' for p in REQUIRED if not (ROOT / p).is_file())
    if not issues:
        identity_meta = (ROOT / REQUIRED[1]).read_text(encoding='utf-8-sig')
        if 'guid: 9b91ecbcc199f4492b9a91e820070131' not in identity_meta:
            issues.append('Mirror NetworkIdentity GUID does not match the Boot scene')
    if issues:
        for issue in issues:
            print(f'FAIL: {issue}', file=sys.stderr)
        return 1
    if args.write_fingerprint:
        with args.write_fingerprint.open('x', encoding='utf-8') as handle:
            json.dump(fingerprints(), handle, indent=2, sort_keys=True)
            handle.write('\n')
    elif args.check_fingerprint:
        if json.loads(args.check_fingerprint.read_text(encoding='utf-8-sig')) != fingerprints():
            print('FAIL: external asset trees differ from the verified build input', file=sys.stderr)
            return 1
    print('PASS: checkout prerequisites' + ('' if args.check_fingerprint else ' (external versions not verified)'))
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (OSError, ValueError, KeyError) as error:
        print(f'FAIL: {error}', file=sys.stderr)
        sys.exit(1)
