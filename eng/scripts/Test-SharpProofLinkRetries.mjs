import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { copyFile, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = fileURLToPath(new URL('../../', import.meta.url));
const scratch = await mkdtemp(path.join(os.tmpdir(), 'sharpproof-link-retries-'));
const requests = new Map();
const server = http.createServer((request, response) => {
  const count = (requests.get(request.url) ?? 0) + 1;
  requests.set(request.url, count);
  const status = request.url.includes('missing') ? 404
    : request.url.includes('persistent') || count === 1 ? 503 : 200;
  response.writeHead(status, { 'Content-Type': 'text/html' });
  response.end('<h1 id="available">Available</h1>');
});

try {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  for (const [name, expectedExit, expectedRequests, expectedDelays] of [
    ['recover', 0, 2, '5\n'],
    ['persistent', 2, 3, '5\n10\n'],
    ['missing', 2, 1, ''],
    ['mixed', 2, 1, ''],
    ['fragment', 2, 1, ''],
  ]) {
    const root = path.join(scratch, name);
    await mkdir(path.join(root, 'bin'), { recursive: true });
    await mkdir(path.join(root, 'reports'));
    await copyFile(path.join(repositoryRoot, 'lychee.toml'), path.join(root, 'lychee.toml'));
    // Skip wall-clock waits while checking the production backoff arguments.
    await writeFile(path.join(root, 'bin/sleep'), '#!/bin/sh\nprintf "%s\\n" "$1" >> delays\n', { mode: 0o755 });
    let input = `[probe](${base}/${name})\n`;
    if (name === 'mixed') input += `[missing](${base}/mixed-missing)\n`;
    if (name === 'fragment') input += '[broken](target.md#absent)\n';
    await writeFile(path.join(root, 'target.md'), '# Target\n');
    await writeFile(path.join(root, 'input.md'), input);
    // Reports must not become link-check inputs on subsequent attempts.
    await writeFile(path.join(root, '.gitignore'), '/reports/\n');
    assert.equal(spawnSync('git', ['init', '--quiet', root]).status, 0);
    const child = spawn('sh', [path.join(repositoryRoot, 'eng/scripts/Test-SharpProofLinks.sh'), 'reports/report.md'], {
      cwd: root,
      env: { ...process.env, GITHUB_TOKEN: '', PATH: `${root}/bin:${process.env.PATH}` },
      stdio: ['ignore', 'pipe', 'pipe'],
      timeout: 60000,
    });
    let output = '';
    child.stdout.on('data', data => { output += data; });
    child.stderr.on('data', data => { output += data; });
    const exitCode = await new Promise((resolve, reject) => {
      child.on('error', reject);
      child.on('exit', resolve);
    });
    assert.equal(exitCode, expectedExit, `${name}: ${output}`);
    assert.equal(requests.get(`/${name}`), expectedRequests, name);
    const delays = await readFile(path.join(root, 'delays'), 'utf8').catch(error => {
      if (error.code !== 'ENOENT') throw error;
      return '';
    });
    assert.equal(delays, expectedDelays, name);
    const report = await readFile(path.join(root, 'reports/report.md'), 'utf8');
    assert.match(report, name === 'recover' ? /Errors\s*\|\s*0\s*\|/ : /Errors\s*\|\s*[1-9]/);
    if (expectedDelays) {
      assert.match(await readFile(path.join(root, 'reports/report.md.attempt-1.md'), 'utf8'), /\[503\]/);
    }
    console.log(`Link retry fixture passed: ${name}`);
  }
} finally {
  server.close();
  await rm(scratch, { recursive: true, force: true });
}
