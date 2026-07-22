import { existsSync, readFileSync, rmSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const stateFile = resolve(root, '.dev/dev-stack.json');

function killPid(pid, name) {
  if (!pid) {
    return;
  }

  try {
    if (process.platform === 'win32') {
      spawnSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'inherit' });
    } else {
      process.kill(pid, 'SIGTERM');
    }

    console.log(`Stopped ${name} (${pid})`);
  } catch (error) {
    if (error.code === 'ESRCH') {
      console.log(`${name} (${pid}) is not running.`);
      return;
    }

    console.warn(`Could not stop ${name} (${pid}): ${error.message}`);
  }
}

function findPidsByPort(port) {
  if (process.platform === 'win32') {
    const result = spawnSync('netstat', ['-ano', '-p', 'tcp'], {
      encoding: 'utf8'
    });

    return result.stdout
      .split(/\r?\n/)
      .filter(line => line.includes(`:${port} `) && line.includes('LISTENING'))
      .map(line => line.trim().split(/\s+/).at(-1))
      .filter(Boolean);
  }

  const result = spawnSync('lsof', ['-tiTCP:' + port, '-sTCP:LISTEN'], {
    encoding: 'utf8'
  });

  return result.stdout
    .split(/\r?\n/)
    .map(value => value.trim())
    .filter(Boolean);
}

function killPort(port, name) {
  const pids = [...new Set(findPidsByPort(port))];

  for (const pid of pids) {
    killPid(Number(pid), `${name} on port ${port}`);
  }
}

if (!existsSync(stateFile)) {
  console.log('No dev stack state file found. Stopping known API/UI ports only.');
  killPort(4200, 'Angular UI');
  killPort(5273, 'Backend API');
  process.exit(0);
}

const state = JSON.parse(readFileSync(stateFile, 'utf8'));
const processes = Array.isArray(state.processes) ? state.processes.toReversed() : [];

for (const processInfo of processes) {
  killPid(processInfo.pid, processInfo.name);
}

rmSync(stateFile, { force: true });
killPort(4200, 'Angular UI');
killPort(5273, 'Backend API');

if (state.managerPid && state.managerPid !== process.pid) {
  killPid(state.managerPid, 'dev stack task');
}

console.log('Development stack stop request completed.');
