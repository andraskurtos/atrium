import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { ensurePostgres } from './ensure-postgres.mjs';
import { COLORS, ROOT_DIR, loadAtriumEnv, logLine, pipePrefixed } from './env.mjs';

const env = loadAtriumEnv();
let apiProcess = null;
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
  if (apiProcess && !apiProcess.killed && apiProcess.exitCode === null) {
    logLine('prod', COLORS.yellow, 'Stopping production server...');
    try {
      process.kill(-apiProcess.pid, 'SIGTERM');
    } catch {
      apiProcess.kill('SIGTERM');
    }
  }
  setTimeout(() => process.exit(exitCode), 500).unref();
}

process.on('SIGINT', () => shutdown(0));
process.on('SIGTERM', () => shutdown(0));

async function main() {
  logLine('prod', COLORS.yellow, 'Building and starting Atrium in production mode...');
  await ensurePostgres(env);

  if (!fs.existsSync(path.join(ROOT_DIR, 'web', 'node_modules'))) {
    logLine('web', COLORS.magenta, 'Installing frontend dependencies...');
    await runStep('npm', ['--prefix', 'web', 'install'], 'web', COLORS.magenta);
  }

  logLine('web', COLORS.magenta, 'Building production PWA bundle...');
  await runStep('npm', ['--prefix', 'web', 'run', 'build'], 'web', COLORS.magenta);

  logLine('api', COLORS.green, 'Building .NET API (Release)...');
  await runStep(
    'dotnet',
    ['build', '-c', 'Release', 'api/Atrium.Api/Atrium.Api.csproj'],
    'api',
    COLORS.green,
  );

  const webDistPath = path.join(ROOT_DIR, 'web', 'dist');
  const prodEnv = {
    ...env,
    ASPNETCORE_ENVIRONMENT: 'Production',
    ASPNETCORE_URLS: `http://localhost:${env.API_PORT}`,
    Atrium__WebDistPath: webDistPath,
  };

  logLine(
    'prod',
    COLORS.yellow,
    `Starting Atrium production server on http://localhost:${env.API_PORT} (serving API + PWA from web/dist)...`,
  );

  apiProcess = spawn(
    'dotnet',
    [
      'run',
      '-c',
      'Release',
      '--no-build',
      '--project',
      'api/Atrium.Api/Atrium.Api.csproj',
      '--no-launch-profile',
    ],
    {
      cwd: ROOT_DIR,
      env: prodEnv,
      detached: true,
      stdio: ['ignore', 'pipe', 'pipe'],
    },
  );
  pipePrefixed(apiProcess, 'api', COLORS.green);

  apiProcess.on('exit', (code, signal) => {
    if (shuttingDown) return;
    logLine(
      'prod',
      COLORS.red,
      `Production server exited (code=${code ?? 'null'}, signal=${signal ?? 'none'}).`,
      process.stderr,
    );
    process.exit(code ?? 1);
  });
}

main().catch((err) => {
  logLine('prod', COLORS.red, err.message, process.stderr);
  shutdown(1);
});

