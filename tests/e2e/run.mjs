// One e2e run: a fresh stack, every spec, then the stack is removed (KEEP_STACK=1 leaves it running to look around).
import { spawnSync } from 'node:child_process';
import { down, up } from './stack.mjs';

await up();
const tests = spawnSync('npx', ['playwright', 'test', ...process.argv.slice(2)], { stdio: 'inherit', shell: true });
if (process.env.KEEP_STACK !== '1') {
  down();
}
process.exit(tests.status ?? 1);
