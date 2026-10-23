declare global {
  interface Window {
    POS_RUNTIME_CONFIG?: { apiOrigin: string };
  }
}

const apiOrigin = window.POS_RUNTIME_CONFIG?.apiOrigin;
if (!apiOrigin) {
  throw new Error('POS_RUNTIME_CONFIG is missing; production API calls are disabled.');
}

export const environment = {
  production: true,
  apiUrl: apiOrigin,
};
