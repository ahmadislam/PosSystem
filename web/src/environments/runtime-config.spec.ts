import { productionApiOrigin } from './runtime-config';

describe('production API configuration', () => {
  it('uses the configured HTTPS origin instead of the frontend origin', () => {
    expect(productionApiOrigin('https://api.example.test/')).toBe('https://api.example.test');
  });

  it.each([undefined, '', 'http://api.example.test', '/api',
    'https://api.example.test/v1', 'https://user:password@api.example.test',
    'https://api.example.test?token=value', 'https://api.example.test#fragment'])
    ('fails closed for invalid or absent configuration: %s', (value) => {
      expect(() => productionApiOrigin(value)).toThrow();
    });
});
