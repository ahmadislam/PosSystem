import { productionApiOrigin } from './runtime-config';

describe('production API configuration', () => {
  it('uses the configured HTTPS origin instead of the frontend origin', () => {
    expect(productionApiOrigin('https://api.example.test/')).toBe('https://api.example.test');
  });

  it('allows loopback HTTP only when it is the browser origin', () => {
    expect(productionApiOrigin(window.location.origin)).toBe(window.location.origin);
    expect(() => productionApiOrigin('http://localhost:9876')).toThrow();
  });

  it.each([undefined, '', 'http://api.example.test', '/api',
    'https://api.example.test/v1', 'https://user:password@api.example.test',
    'https://api.example.test?token=value', 'https://api.example.test#fragment'])
    ('fails closed for invalid or absent configuration: %s', (value) => {
      expect(() => productionApiOrigin(value)).toThrow();
    });
});
