import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const stateDir = resolve(root, '.dev');
const stateFile = resolve(stateDir, 'dev-stack.json');

const processes = [];
const state = {
  managerPid: process.pid,
  startedAt: new Date().toISOString(),
  processes: []
};

function delay(ms) {
  return new Promise(resolveDelay => setTimeout(resolveDelay, ms));
}

async function isHttpReady(url) {
  try {
    const response = await fetch(url, { signal: AbortSignal.timeout(1500) });
    return response.ok;
  } catch {
    return false;
  }
}

async function waitFor(name, url, timeoutMs = 60_000) {
  const startedAt = Date.now();

  while (Date.now() - startedAt < timeoutMs) {
    if (await isHttpReady(url)) {
      console.log(`${name} is ready: ${url}`);
      return;
    }

    await delay(1000);
  }

  throw new Error(`${name} did not become ready within ${timeoutMs / 1000}s: ${url}`);
}

function persistState() {
  if (!existsSync(stateDir)) {
    mkdirSync(stateDir, { recursive: true });
  }

  writeFileSync(stateFile, `${JSON.stringify(state, null, 2)}\n`);
}

function startProcess(name, command, args, options = {}) {
  console.log(`Starting ${name}...`);

  const child = spawn(command, args, {
    cwd: options.cwd ?? root,
    env: { ...process.env, ...options.env },
    shell: process.platform === 'win32',
    stdio: 'inherit'
  });

  processes.push(child);
  state.processes.push({
    name,
    pid: child.pid,
    command,
    args,
    startedByScript: true
  });
  persistState();

  child.on('exit', (code, signal) => {
    console.log(`${name} exited with code ${code ?? 'null'} signal ${signal ?? 'null'}`);
  });

  return child;
}

function shutdown() {
  console.log('Stopping development stack...');

  for (const child of processes.toReversed()) {
    if (!child.killed) {
      child.kill(process.platform === 'win32' ? undefined : 'SIGTERM');
    }
  }

  setTimeout(() => process.exit(0), 1000).unref();
}

process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);

persistState();

if (!(await isHttpReady('http://localhost:11434/api/tags'))) {
  startProcess('ollama', 'ollama', ['serve']);
  await waitFor('Ollama', 'http://localhost:11434/api/tags');
} else {
  console.log('Ollama is already running: http://localhost:11434');
}

if (!(await isHttpReady('http://localhost:5273/swagger/index.html'))) {
  startProcess('backend', 'dotnet', [
    'run',
    '--project',
    'backend/src/Chatbot.Api/Chatbot.Api.csproj',
    '--launch-profile',
    'http'
  ]);
  await waitFor('Backend API', 'http://localhost:5273/swagger/index.html');
} else {
  console.log('Backend API is already running: http://localhost:5273');
}

if (!(await isHttpReady('http://localhost:4200'))) {
  startProcess('frontend', 'npm', ['start'], {
    cwd: resolve(root, 'frontend/chatbot-ui')
  });
  await waitFor('Angular UI', 'http://localhost:4200');
} else {
  console.log('Angular UI is already running: http://localhost:4200');
}

console.log('');
console.log('Development stack is running.');
console.log('Frontend: http://localhost:4200');
console.log('API Swagger: http://localhost:5273/swagger');
console.log('');
console.log('Keep this task running. Use the "Stop dev stack" VS Code task to stop started processes.');

await new Promise(() => {});
