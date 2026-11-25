import test from 'node:test';
import assert from 'node:assert/strict';
import { auditContent } from './check-suppressions.mjs';

test('rejects undocumented C# pragma warning disable', () => {
  const res = auditContent('api/Sample.cs', '#pragma warning disable CS8600\nvar x = null;');
  assert.equal(res.length, 1);
  assert.equal(res[0].rule, 'documented-pragma-disable');
});

test('allows C# pragma warning disable with inline Justification', () => {
  const res = auditContent(
    'api/Sample.cs',
    '#pragma warning disable CS8600 // Justification: third-party serializer returns nullable wrapper\nvar x = null;',
  );
  assert.equal(res.length, 0);
});

test('rejects C# SuppressMessage without Justification', () => {
  const res = auditContent('api/Sample.cs', '[SuppressMessage("Design", "CA1031")]\npublic void F() {}');
  assert.equal(res.length, 1);
  assert.equal(res[0].rule, 'documented-suppress-message');
});

test('allows C# SuppressMessage with non-empty Justification', () => {
  const res = auditContent(
    'api/Sample.cs',
    '[SuppressMessage("Design", "CA1031", Justification = "Health probe catches all DB transport errors")]\npublic void F() {}',
  );
  assert.equal(res.length, 0);
});

test('rejects undocumented MSBuild NoWarn and TreatWarningsAsErrors=false', () => {
  const res = auditContent(
    'api/Test.csproj',
    '<PropertyGroup>\n  <NoWarn>CS1591</NoWarn>\n  <TreatWarningsAsErrors>false</TreatWarningsAsErrors>\n</PropertyGroup>',
  );
  assert.equal(res.length, 2);
});

test('allows MSBuild NoWarn with preceding Justification comment', () => {
  const res = auditContent(
    'api/Test.csproj',
    '<PropertyGroup>\n  <!-- Justification: generated migration snapshots omit XML docs -->\n  <NoWarn>CS1591</NoWarn>\n</PropertyGroup>',
  );
  assert.equal(res.length, 0);
});

test('bans @ts-ignore and @ts-nocheck outright', () => {
  const res = auditContent(
    'web/src/a.ts',
    '// @ts-nocheck\n// @ts-ignore Justification: even with comment it is banned\nconst x = 1;',
  );
  assert.equal(res.length, 2);
});

test('requires Justification on @ts-expect-error and eslint-disable', () => {
  const bad = auditContent('web/src/a.ts', '// @ts-expect-error\n// eslint-disable-next-line\nconst x = 1;');
  assert.equal(bad.length, 2);

  const good = auditContent(
    'web/src/a.ts',
    '// @ts-expect-error Justification: testing invalid payload shape\n// eslint-disable-next-line no-console -- Justification: CLI logger\nconst x = 1;',
  );
  assert.equal(good.length, 0);
});
