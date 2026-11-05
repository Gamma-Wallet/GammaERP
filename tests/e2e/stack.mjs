// The e2e stack: deploy/docker-compose.yml as its own compose project, on its own ports, with a database that starts empty.
//   node stack.mjs up     builds the images, starts on a fresh database, waits until the screens answer
//   node stack.mjs down   stops it and removes its volumes
import { spawnSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
export const PROJECT = 'gamma-erp-e2e';
export const BASE_URL = 'http://127.0.0.1:8091';
const compose = ['compose', '-p', PROJECT, '-f', join(here, '..', '..', 'deploy', 'docker-compose.yml'), '--env-file', join(here, 'e2e.env')];

function docker(...args) {
  const run = spawnSync('docker', [...compose, ...args], { stdio: 'inherit' });
  if (run.status !== 0) {
    throw new Error(`docker ${args.join(' ')} failed (${run.status}).`);
  }
}

export async function up() {
  docker('down', '-v', '--remove-orphans');
  docker('up', '-d', '--build', '--wait');
  for (let i = 0; i < 60; i++) {
    const health = await fetch(`${BASE_URL}/health`).then(r => r.text()).catch(() => '');
    if (health.includes('Healthy')) {
      return;
    }
    await new Promise(r => setTimeout(r, 1000));
  }
  throw new Error(`${BASE_URL} did not become healthy.`);
}

export function down() {
  docker('down', '-v', '--remove-orphans');
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const command = process.argv[2];
  if (command === 'up') {
    await up();
  } else if (command === 'down') {
    down();
  } else {
    console.error('node stack.mjs up | down');
    process.exit(2);
  }
}
