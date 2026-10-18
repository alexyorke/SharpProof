"""Fail closed when a source build differs from the enrolled native payload."""
import hashlib
import json
import pathlib
import shutil
import sys

catalog = json.loads(pathlib.Path(sys.argv[1]).read_text())['z3']
output = pathlib.Path(sys.argv[2])
published = pathlib.Path(sys.argv[3])
provenance = catalog['windowsCrossBuild']
for rid, catalog_key, machine in (
    ('win-x64', 'windows', '0x8664'),
    ('win-arm64', 'windowsArm64', '0xaa64'),
):
    expected = catalog[catalog_key]
    payload = output / rid
    binary = (payload / 'libz3.dll').read_bytes()
    receipt = json.loads((payload / 'payload-receipt.json').read_text())
    digest = hashlib.sha256(binary).hexdigest()
    if len(binary) != expected['libraryBytes'] or digest != expected['librarySha256']:
        raise SystemExit(f'{rid}: source build differs from pinned bytes/SHA256: {len(binary)} / {digest}')
    for key in ('sourceArchiveSha256', 'sourceCommit', 'compilerArchiveSha256', 'sourceDateEpoch'):
        if receipt[key] != provenance[key]:
            raise SystemExit(f'{rid}: receipt provenance mismatch: {key}')
    if (receipt['rid'], receipt['peMachine'], receipt['peTimestamp'], receipt['libraryBytes'], receipt['librarySha256'], receipt['sourceModified']) != (rid, machine, 0, len(binary), digest, False):
        raise SystemExit(f'{rid}: invalid native payload receipt')
    target = published / rid
    target.mkdir(parents=True, exist_ok=True)
    for name in ('libz3.dll', 'payload-receipt.json'):
        shutil.copyfile(payload / name, target / name)
    print(f'{rid}: verified pinned source build {digest}')
shutil.copytree(output / 'licenses', published / 'licenses', dirs_exist_ok=True)
