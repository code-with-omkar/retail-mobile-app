import { crashReportingDsn, crashReportingProviders, scrubEvent } from './crash-reporting';

describe('crash reporting', () => {
  it('is off until a key is set', () => {
    expect(crashReportingDsn(null)).toBeNull();
    expect(crashReportingDsn({})).toBeNull();
    expect(crashReportingDsn({ __SENTRY_DSN__: '   ' })).toBeNull();
    expect(crashReportingDsn({ __SENTRY_DSN__: 42 })).toBeNull();
    expect(crashReportingDsn({ __SENTRY_DSN__: ' https://k@example.test/1 ' })).toBe('https://k@example.test/1');
    expect(crashReportingProviders({})).toEqual([]);
  });

  it('removes the person, the request details and typed values from an event, and keeps the error', () => {
    const event = {
      type: undefined,
      message: 'boom',
      user: { id: 'u1', email: 'a@b.test', ip_address: '1.2.3.4' },
      request: { url: 'http://x/orders/123', headers: { Authorization: 'Bearer s' }, cookies: { a: 'b' }, data: 'x', query_string: 'token=s' },
      breadcrumbs: [
        { category: 'ui.click', message: 'button', data: { url: '/orders/123' } },
        { category: 'ui.input', message: 'phone 9876543210' },
      ],
    } as never as Parameters<typeof scrubEvent>[0];

    const scrubbed = scrubEvent(event);

    expect(scrubbed.user).toEqual({});
    expect(scrubbed.request?.headers).toBeUndefined();
    expect(scrubbed.request?.cookies).toBeUndefined();
    expect(scrubbed.request?.data).toBeUndefined();
    expect(scrubbed.request?.query_string).toBeUndefined();
    expect(scrubbed.breadcrumbs).toHaveLength(1);
    expect(scrubbed.breadcrumbs?.[0].data).toBeUndefined();
    expect(scrubbed.message).toBe('boom');
  });
});
