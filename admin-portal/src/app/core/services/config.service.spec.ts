import { TestBed } from '@angular/core/testing';
import { ConfigService } from './config.service';

describe('ConfigService', () => {
  it('joins the base url and the endpoint, and follows a new base url', () => {
    const config = TestBed.inject(ConfigService);
    config.setBaseUrl('http://api.test/api');
    expect(config.getBaseUrl()).toBe('http://api.test/api');
    expect(config.getUrl('/auth/login')).toBe('http://api.test/api/auth/login');
  });

  it('allows the timeout to be changed', () => {
    const config = TestBed.inject(ConfigService);
    config.setTimeout(5000);
    expect(config.getTimeout()).toBe(5000);
  });
});
