import { readdirSync, readFileSync } from 'node:fs';

/** Detect builds that copy runtime-config.js but still compile the development environment. */
export function verifyProductionBuild(outputDirectory, origin) {
  const index = readFileSync(new URL('index.html', outputDirectory), 'utf8');
  const configPosition = index.search(/<script\s+src="runtime-config\.js"/);
  const modulePosition = index.search(/<script[^>]*type="module"/);
  if (configPosition < 0 || modulePosition < 0 || configPosition > modulePosition) {
    throw new Error('Production runtime configuration must load before the application module.');
  }
  const runtimeConfig = readFileSync(new URL('runtime-config.js', outputDirectory), 'utf8');
  if (runtimeConfig !== `window.POS_RUNTIME_CONFIG = Object.freeze(${JSON.stringify({ apiOrigin: origin })});\n`) {
    throw new Error('Built runtime configuration does not match POS_API_ORIGIN.');
  }
  const usesRuntimeConfig = readdirSync(outputDirectory)
    .filter((name) => name.endsWith('.js') && name !== 'runtime-config.js')
    .some((name) => readFileSync(new URL(name, outputDirectory), 'utf8').includes('POS_RUNTIME_CONFIG'));
  if (!usesRuntimeConfig) {
    throw new Error('Application bundle ignores runtime configuration. Check production environment file replacements.');
  }
}
