import { spawn } from 'node:child_process';
import { ensurePostgres } from './ensure-postgres.mjs';
import { COLORS, ROOT_DIR, loadAtriumEnv, logLine } from './env.mjs';

const env = loadAtriumEnv();
const scriptOnly = process.argv.includes('--script');

async function main() {
  if (!scriptOnly && !process.env.ConnectionStrings__DefaultConnection) {
    await ensurePostgres(env);
  }

  const dotnetArgs = [
    'run',
    '--project',
    'api/Atrium.Api/Atrium.Api.csproj',
    '--no-launch-profile',
    '--',
    scriptOnly ? '--migration-script' : '--migrate',
  ];

  const runEnv = {
    ...env,
    ASPNETCORE_ENVIRONMENT: process.env.ASPNETCORE_ENVIRONMENT || 'Development',
    Atrium__Database__AutoMigrate: 'false',
  };

  if (!scriptOnly) {
    logLine('db', COLORS.cyan, 'Applying EF Core migrations to PostgreSQL...');
  }

  await new Promise((resolve, reject) => {
    const child = spawn('dotnet', dotnetArgs, {
      cwd: ROOT_DIR,
      env: runEnv,
      stdio: 'inherit',
    });
    child.on('error', reject);
    child.on('exit', (code) => {
      if (code === 0) resolve();
      else reject(new Error(`Migration command exited with code ${code}`));
    });
  });
}

main().catch((err) => {
  logLine('db', COLORS.red, err.message, process.stderr);
  process.exit(1);
});
