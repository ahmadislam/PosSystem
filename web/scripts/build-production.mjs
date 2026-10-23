import { spawnSync } from 'node:child_process';
import { writeFileSync, rmSync } from 'node:fs';
import { verifyProductionBuild } from './verify-production-build.mjs';

const configuredOrigin = process.env.POS_API_ORIGIN;
if (!configuredOrigin) {
  throw new Error('Set POS_API_ORIGIN to the deployed HTTPS API origin before a production build.');
}

const apiUrl = new URL(configuredOrigin);
if (apiUrl.protocol !== 'https:' || apiUrl.origin !== configuredOrigin.replace(/\/$/, '')) {
  throw new Error('POS_API_ORIGIN must be an HTTPS origin without a path, query, or fragment.');
}

const runtimeConfigPath = new URL('../public/runtime-config.js', import.meta.url);
writeFileSync(
  runtimeConfigPath,
  `window.POS_RUNTIME_CONFIG = Object.freeze(${JSON.stringify({ apiOrigin: apiUrl.origin })});\n`,
);

try {
  const result = spawnSync('ng', ['build', '--configuration', 'production'], {
    stdio: 'inherit',
    shell: process.platform === 'win32',
  });
  if (result.error) throw result.error;
  if (result.status !== 0) process.exitCode = result.status ?? 1;
  else verifyProductionBuild(new URL('../dist/web/browser/', import.meta.url), apiUrl.origin);
} finally {
  rmSync(runtimeConfigPath, { force: true });
}
