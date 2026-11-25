import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT_DIR = path.resolve(__dirname, '..');

const SCAN_ROOTS = ['api', 'web/src', 'scripts'];
const IGNORED_DIRS = new Set(['bin', 'obj', 'node_modules', 'dist', 'dev-dist', '.git']);
const ALLOWED_EXTS = new Set(['.cs', '.csproj', '.props', '.ts', '.tsx', '.js', '.jsx', '.mjs']);

const JUSTIFICATION_RE = /Justification:\s*\S+/i;
const SUPPRESS_MESSAGE_JUSTIFICATION_RE = /Justification\s*=\s*"[^"]*\S[^"]*"/;

export function auditContent(filePath, content) {
  const violations = [];
  const lines = content.split(/\r?\n/);
  const ext = path.extname(filePath).toLowerCase();

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const prevLine = i > 0 ? lines[i - 1] : '';
    const lineNum = i + 1;

    // 1. Banned blanket suppressions in JS/TS
    if (['.ts', '.tsx', '.js', '.jsx', '.mjs'].includes(ext)) {
      if (/@ts-ignore\b/.test(line)) {
        violations.push({
          file: filePath,
          line: lineNum,
          rule: 'no-ts-ignore',
          message: '`@ts-ignore` is forbidden. Fix the type error or use `@ts-expect-error // Justification: <reason>`.',
        });
        continue;
      }
      if (/@ts-nocheck\b/.test(line)) {
        violations.push({
          file: filePath,
          line: lineNum,
          rule: 'no-ts-nocheck',
          message: '`@ts-nocheck` is forbidden. Type errors must be fixed, not silenced file-wide.',
        });
        continue;
      }
      if (/@ts-expect-error\b/.test(line) && !JUSTIFICATION_RE.test(line) && !JUSTIFICATION_RE.test(prevLine)) {
        violations.push({
          file: filePath,
          line: lineNum,
          rule: 'documented-ts-expect-error',
          message: '`@ts-expect-error` requires an inline `Justification: <reason>` comment.',
        });
      }
      if (/eslint-disable(?:-next-line|-line)?\b/.test(line) && !JUSTIFICATION_RE.test(line) && !JUSTIFICATION_RE.test(prevLine)) {
        violations.push({
          file: filePath,
          line: lineNum,
          rule: 'documented-eslint-disable',
          message: '`eslint-disable` requires an inline `Justification: <reason>` comment.',
        });
      }
    }

    // 2. C# source suppressions
    if (ext === '.cs') {
      if (/^\s*#pragma\s+warning\s+disable\b/.test(line) && !JUSTIFICATION_RE.test(line) && !JUSTIFICATION_RE.test(prevLine)) {
        violations.push({
          file: filePath,
          line: lineNum,
          rule: 'documented-pragma-disable',
          message: '`#pragma warning disable` requires a `// Justification: <reason>` comment on the same or preceding line.',
        });
      }
      if (/\[(?:global::)?(?:System\.Diagnostics\.CodeAnalysis\.)?SuppressMessage\b/.test(line)) {
        // Check current line and next 3 lines for Justification = "..."
        const windowText = lines.slice(i, Math.min(lines.length, i + 4)).join(' ');
        if (!SUPPRESS_MESSAGE_JUSTIFICATION_RE.test(windowText)) {
          violations.push({
            file: filePath,
            line: lineNum,
            rule: 'documented-suppress-message',
            message: '`[SuppressMessage]` must include a non-empty `Justification = "..."` argument.',
          });
        }
      }
    }

    // 3. MSBuild XML suppressions (.csproj / .props)
    if (ext === '.csproj' || ext === '.props') {
      if (/<TreatWarningsAsErrors>\s*false\s*<\/TreatWarningsAsErrors>/i.test(line)) {
        violations.push({
          file: filePath,
          line: lineNum,
          rule: 'no-disable-warnaserror',
          message: 'Disabling `<TreatWarningsAsErrors>` is forbidden.',
        });
      }
      if (/<NoWarn\b/i.test(line) && !JUSTIFICATION_RE.test(line) && !JUSTIFICATION_RE.test(prevLine)) {
        violations.push({
          file: filePath,
          line: lineNum,
          rule: 'documented-nowarn',
          message: '`<NoWarn>` requires a `<!-- Justification: <reason> -->` comment on the same or preceding line.',
        });
      }
    }
  }

  return violations;
}

function collectFiles(dir, out = []) {
  if (!fs.existsSync(dir)) return out;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (IGNORED_DIRS.has(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      collectFiles(full, out);
    } else if (ALLOWED_EXTS.has(path.extname(entry.name).toLowerCase())) {
      // Skip auditing the auditor script and its test fixtures
      const rel = path.relative(ROOT_DIR, full).replace(/\\/g, '/');
      if (rel === 'scripts/check-suppressions.mjs' || rel === 'scripts/check-suppressions.test.mjs') {
        continue;
      }
      out.push(full);
    }
  }
  return out;
}

export function runAudit(rootDir = ROOT_DIR) {
  const files = SCAN_ROOTS.flatMap((rel) => collectFiles(path.join(rootDir, rel)));
  const allViolations = [];
  for (const file of files) {
    const rel = path.relative(rootDir, file).replace(/\\/g, '/');
    const content = fs.readFileSync(file, 'utf8');
    allViolations.push(...auditContent(rel, content));
  }
  return { filesScanned: files.length, violations: allViolations };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const { filesScanned, violations } = runAudit();
  if (violations.length > 0) {
    console.error(`Suppression audit FAILED (${violations.length} undocumented or forbidden suppression(s) across ${filesScanned} files):\n`);
    for (const v of violations) {
      console.error(`  ${v.file}:${v.line} [${v.rule}] ${v.message}`);
    }
    console.error('\nSee docs/engineering/contributing-and-ci.md for the warning justification policy.');
    process.exit(1);
  }
  console.log(`Suppression audit passed (${filesScanned} files scanned, 0 undocumented suppressions).`);
}
