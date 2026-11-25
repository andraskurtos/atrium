import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { ensurePostgres } from './ensure-postgres.mjs';
import { COLORS, ROOT_DIR, loadAtriumEnv, logLine, pipePrefixed } from './env.mjs';

const env = loadAtriumEnv();
const children = [];
let shuttingDown = false;

function runStep(cmd, args, label, color, stepEnv = env) {
  return new Promise((resolve, reject) => {
    const child = spawn(cmd, args, {
      cwd: ROOT_DIR,
      env: stepEnv,
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    pipePrefixed(child, label, color);
    child.on('error', reject);
    child.on('exit', (code) => {
      if (code === 0) resolve();
      else reject(new Error(`${cmd} ${args.join(' ')} exited with code ${code}`));
    });
  });
}

function shutdown(exitCode = 0) {
  if (shuttingDown) return;
  shuttingDown = true;
  logLine('dev', COLORS.yellow, 'Shutting down dev services...');
  for (const child of children) {
    if (!child.killed && child.exitCode === null) {
      try {
        process.kill(-child.pid, 'SIGTERM');
      } catch {
        child.kill('SIGTERM');
      }
    }
  }
  setTimeout(() => process.exit(exitCode), 500).unref();
}

process.on('SIGINT', () => shutdown(0));
process.on('SIGTERM', () => shutdown(0));

async function main() {
  logLine('dev', COLORS.yellow, 'Starting Atrium development environment...');
  await ensurePostgres(env);

  if (!fs.existsSync(path.join(ROOT_DIR, 'web', 'node_modules'))) {
    logLine('web', COLORS.magenta, 'Installing frontend dependencies...');
    await runStep('npm', ['--prefix', 'web', 'install'], 'web', COLORS.magenta);
  }

  const apiEnv = {
    ...env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: `http://localhost:${env.API_PORT}`,
    DOTNET_WATCH_RESTART_ON_RUDE_EDIT: 'true',
  };

  logLine('api', COLORS.green, `Starting .NET API (watch) on http://localhost:${env.API_PORT}...`);
  const api = spawn(
    'dotnet',
    [
      'watch',
      'run',
      '--non-interactive',
      '--project',
      'api/Atrium.Api/Atrium.Api.csproj',
      '--no-launch-profile',
    ],
    {
      cwd: ROOT_DIR,
      env: apiEnv,
      detached: true,
      stdio: ['ignore', 'pipe', 'pipe'],
    },
  );
  children.push(api);
  pipePrefixed(api, 'api', COLORS.green);

  logLine('web', COLORS.magenta, 'Starting Vite PWA dev server on http://localhost:5173...');
  const web = spawn('npm', ['--prefix', 'web', 'run', 'dev'], {
    cwd: ROOT_DIR,
    env,
    detached: true,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  children.push(web);
  pipePrefixed(web, 'web', COLORS.magenta);

  const onChildExit = (name) => (code, signal) => {
    if (shuttingDown) return;
    logLine(
      'dev',
      COLORS.red,
      `${name} exited unexpectedly (code=${code ?? 'null'}, signal=${signal ?? 'none'}).`,
      process.stderr,
    );
    shutdown(code ?? 1);
  };

  api.on('exit', onChildExit('api'));
  web.on('exit', onChildExit('web'));
}

main().catch((err) => {
  logLine('dev', COLORS.red, err.message, process.stderr);
  shutdown(1);
});

