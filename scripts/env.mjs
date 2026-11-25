import fs from 'node:fs';
import path from 'node:path';
import readline from 'node:readline';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
export const ROOT_DIR = path.resolve(__dirname, '..');

function parseEnvFile(filePath) {
  if (!fs.existsSync(filePath)) return {};
  const result = {};
  const content = fs.readFileSync(filePath, 'utf8');
  for (const rawLine of content.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith('#')) continue;
    const eqIndex = line.indexOf('=');
    if (eqIndex === -1) continue;
    const key = line.slice(0, eqIndex).trim();
    let value = line.slice(eqIndex + 1).trim();
    if (
      (value.startsWith('"') && value.endsWith('"')) ||
      (value.startsWith("'") && value.endsWith("'"))
    ) {
      value = value.slice(1, -1);
    }
    result[key] = value;
  }
  return result;
}

export function loadAtriumEnv() {
  const defaults = parseEnvFile(path.join(ROOT_DIR, '.env.example'));
  const local = parseEnvFile(path.join(ROOT_DIR, '.env'));

  const merged = {
    POSTGRES_DB: 'atrium',
    POSTGRES_USER: 'atrium',
    POSTGRES_PASSWORD: 'atrium_dev_password',
    POSTGRES_PORT: '5433',
    API_PORT: '5000',
    ...defaults,
    ...local,
    ...process.env,
  };

  if (!merged.ConnectionStrings__DefaultConnection) {
    merged.ConnectionStrings__DefaultConnection =
      `Host=127.0.0.1;Port=${merged.POSTGRES_PORT};` +
      `Database=${merged.POSTGRES_DB};Username=${merged.POSTGRES_USER};` +
      `Password=${merged.POSTGRES_PASSWORD}`;
  }

  if (!merged.Atrium__CorsOrigins && merged.CORS_ORIGINS) {
    merged.Atrium__CorsOrigins = merged.CORS_ORIGINS;
  }

  return merged;
}

export const COLORS = {
  reset: '\x1b[0m',
  cyan: '\x1b[36m',
  green: '\x1b[32m',
  magenta: '\x1b[35m',
  yellow: '\x1b[33m',
  red: '\x1b[31m',
};

export function logLine(label, color, message, stream = process.stdout) {
  stream.write(`${color}[${label}]${COLORS.reset} ${message}\n`);
}

export function pipePrefixed(child, label, color) {
  if (child.stdout) {
    const rlOut = readline.createInterface({ input: child.stdout });
    rlOut.on('line', (line) => logLine(label, color, line, process.stdout));
  }
  if (child.stderr) {
    const rlErr = readline.createInterface({ input: child.stderr });
    rlErr.on('line', (line) => logLine(label, color, line, process.stderr));
  }
}

