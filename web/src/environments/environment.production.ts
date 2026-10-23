import { productionApiOrigin } from './runtime-config';

declare global {
  interface Window {
    POS_RUNTIME_CONFIG?: { apiOrigin: string };
  }
}

export const environment = {
  production: true,
  apiUrl: productionApiOrigin(window.POS_RUNTIME_CONFIG?.apiOrigin),
};
