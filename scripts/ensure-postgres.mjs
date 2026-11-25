import { spawn, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { COLORS, ROOT_DIR, loadAtriumEnv, logLine, pipePrefixed } from './env.mjs';

function runCommandSync(cmd, args, env) {
  return spawnSync(cmd, args, {
    cwd: ROOT_DIR,
    env,
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
  });
}

export async function ensurePostgres(env = loadAtriumEnv()) {
  const dockerCheck = runCommandSync('docker', ['info', '--format', '{{.ServerVersion}}'], env);
  if (dockerCheck.status !== 0) {
    logLine(
      'db',
      COLORS.red,
      'Docker daemon is not reachable. Start Docker (e.g. `sudo systemctl start docker`) and try again.',
      process.stderr,
    );
    const detail = (dockerCheck.stderr || dockerCheck.stdout || '').trim();
    if (detail) logLine('db', COLORS.red, detail, process.stderr);
    throw new Error('Docker daemon is not running');
  }

  const runningCheck = runCommandSync(
    'docker',
    ['compose', 'ps', '--status', 'running', '--services'],
    env,
  );
  const runningServices = (runningCheck.stdout || '')
    .split(/\r?\n/)
    .map((s) => s.trim())
    .filter(Boolean);

  if (runningServices.includes('postgres')) {
    const healthCheck = runCommandSync(
      'docker',
      ['inspect', '--format', '{{if .State.Health}}{{.State.Health.Status}}{{else}}running{{end}}', 'atrium-postgres'],
      env,
    );
    const status = (healthCheck.stdout || '').trim();
    if (status === 'healthy' || status === 'running') {
      logLine(
        'db',
        COLORS.cyan,
        `PostgreSQL is already running (${status}) on 127.0.0.1:${env.POSTGRES_PORT}.`,
      );
      return;
    }
    logLine('db', COLORS.yellow, `PostgreSQL container state is "${status}", waiting until healthy...`);
  } else {
    logLine('db', COLORS.cyan, 'PostgreSQL is not running. Starting container via docker compose...');
  }

  await new Promise((resolve, reject) => {
    const up = spawn('docker', ['compose', 'up', '-d', '--wait', 'postgres'], {
      cwd: ROOT_DIR,
      env,
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    pipePrefixed(up, 'db', COLORS.cyan);
    up.on('error', reject);
    up.on('exit', (code) => {
      if (code === 0) {
        logLine('db', COLORS.cyan, `PostgreSQL is ready on 127.0.0.1:${env.POSTGRES_PORT}.`);
        resolve();
      } else {
        reject(new Error(`docker compose up exited with code ${code}`));
      }
    });
  });
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  ensurePostgres().catch((err) => {
    logLine('db', COLORS.red, err.message, process.stderr);
    process.exit(1);
  });
}

