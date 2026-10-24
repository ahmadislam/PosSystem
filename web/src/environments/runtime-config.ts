/** Production never falls back to the SPA origin when API configuration is absent. */
export function productionApiOrigin(value: unknown): string {
  if (typeof value !== 'string' || !value) {
    throw new Error('POS_RUNTIME_CONFIG is missing; production API calls are disabled.');
  }

  let parsed: URL;
  try {
    parsed = new URL(value);
  } catch {
    throw new Error('POS_RUNTIME_CONFIG.apiOrigin must be an HTTPS origin.');
  }
  // A combined container can be exercised locally over HTTP. Public deployments
  // and all cross-origin APIs still require HTTPS.
  const localSameOrigin = parsed.protocol === 'http:' && parsed.origin === window.location.origin
    && ['localhost', '127.0.0.1', '[::1]'].includes(parsed.hostname);
  if ((!localSameOrigin && parsed.protocol !== 'https:') || parsed.origin !== value.replace(/\/$/, '')) {
    throw new Error('POS_RUNTIME_CONFIG.apiOrigin must be an HTTPS origin without credentials, path, query, or fragment.');
  }
  return parsed.origin;
}
