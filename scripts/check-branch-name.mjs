import { fileURLToPath } from 'node:url';

export const ALLOWED_BRANCH_RE = /^(feature|bugfix)\/[a-z0-9][a-z0-9._-]*$/;

export function isValidBranchName(branch) {
  return typeof branch === 'string' && ALLOWED_BRANCH_RE.test(branch);
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const branch = process.argv[2] || '';
  if (!isValidBranchName(branch)) {
    console.error(`Invalid branch name: "${branch}"`);
    console.error('Branches targeting main must match: feature/<slug> or bugfix/<slug>');
    console.error('  Allowed characters after prefix: lowercase letters, digits, ".", "_", "-"');
    console.error('  Examples: feature/passkey-auth, bugfix/health-503-status');
    process.exit(1);
  }
  console.log(`Branch name valid: ${branch}`);
}
