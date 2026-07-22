import { existsSync, readFileSync } from 'node:fs';
import { spawn, spawnSync } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const appSettingsPath = resolve(root, 'backend/src/Chatbot.Api/appsettings.json');

function run(command, args, options = {}) {
  return spawnSync(command, args, {
    cwd: options.cwd ?? root,
    stdio: options.stdio ?? 'pipe',
    encoding: 'utf-8',
    ...options
  });
}

function delay(ms) {
  return new Promise(resolveDelay => setTimeout(resolveDelay, ms));
}

async function isOllamaHttpReady() {
  try {
    const response = await fetch('http://127.0.0.1:11434/api/tags', {
      signal: AbortSignal.timeout(1500)
    });
    return response.ok;
  } catch {
    return false;
  }
}

async function waitForOllama(timeoutMs = 30_000) {
  const startedAt = Date.now();

  while (Date.now() - startedAt < timeoutMs) {
    if (await isOllamaHttpReady()) {
      return true;
    }

    await delay(1000);
  }

  return false;
}

function parseConfiguredChatModel() {
  if (!existsSync(appSettingsPath)) {
    return null;
  }

  try {
    const raw = readFileSync(appSettingsPath, 'utf-8');
    const json = JSON.parse(raw);
    return json?.Ollama?.ChatModel ?? null;
  } catch {
    return null;
  }
}

function ensureOllamaInstalled() {
  const versionCheck = run('ollama', ['--version']);
  if (versionCheck.status === 0) {
    const version = (versionCheck.stdout || versionCheck.stderr || '').trim();
    console.log(`Ollama is already installed (${version}).`);
    return;
  }

  if (process.platform !== 'win32') {
    throw new Error('Ollama is not installed. Please install it first: https://ollama.com/download');
  }

  console.log('Ollama was not found. Installing with winget...');
  const install = run('winget', [
    'install',
    '--id',
    'Ollama.Ollama',
    '-e',
    '--accept-source-agreements',
    '--accept-package-agreements'
  ], { stdio: 'inherit' });

  if (install.status !== 0) {
    throw new Error('Failed to install Ollama via winget. Install manually from https://ollama.com/download');
  }

  const recheck = run('ollama', ['--version']);
  if (recheck.status !== 0) {
    throw new Error('Ollama installation finished but CLI is still unavailable. Open a new terminal and re-run this script.');
  }

  const version = (recheck.stdout || recheck.stderr || '').trim();
  console.log(`Ollama installed successfully (${version}).`);
}

async function ensureOllamaRunning() {
  if (await isOllamaHttpReady()) {
    console.log('Ollama server is already running.');
    return;
  }

  console.log('Starting Ollama server...');
  const child = spawn('ollama', ['serve'], {
    cwd: root,
    detached: true,
    stdio: 'ignore'
  });
  child.unref();

  const ready = await waitForOllama();
  if (!ready) {
    throw new Error('Ollama did not become reachable at http://127.0.0.1:11434 within 30s.');
  }

  console.log('Ollama server is running.');
}

function listInstalledModels() {
  const list = run('ollama', ['list']);
  if (list.status !== 0) {
    throw new Error(`Failed to list Ollama models. ${list.stderr?.trim() ?? ''}`.trim());
  }

  const lines = (list.stdout ?? '')
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter(Boolean);

  if (lines.length <= 1) {
    return new Set();
  }

  const names = lines
    .slice(1)
    .map(line => line.split(/\s+/)[0])
    .filter(Boolean);

  return new Set(names);
}

function getDesiredModels() {
  const fromArgs = process.argv.slice(2).filter(Boolean);
  if (fromArgs.length > 0) {
    return [...new Set(fromArgs)];
  }

  const configured = parseConfiguredChatModel();
  const defaults = [configured, 'nomic-embed-text:latest'].filter(Boolean);

  return [...new Set(defaults)];
}

function pullModel(model) {
  console.log(`Pulling model ${model}...`);
  const pull = run('ollama', ['pull', model], { stdio: 'inherit' });

  if (pull.status !== 0) {
    throw new Error(`Failed to pull model ${model}.`);
  }
}

async function main() {
  ensureOllamaInstalled();
  await ensureOllamaRunning();

  const models = getDesiredModels();
  if (models.length === 0) {
    console.log('No models requested. Nothing to pull.');
    return;
  }

  const installed = listInstalledModels();

  for (const model of models) {
    if (installed.has(model)) {
      console.log(`Model already present: ${model}`);
      continue;
    }

    pullModel(model);
  }

  console.log('Ollama setup complete.');
  console.log(`Models checked: ${models.join(', ')}`);
}

main().catch(error => {
  console.error(error.message || error);
  process.exit(1);
});
