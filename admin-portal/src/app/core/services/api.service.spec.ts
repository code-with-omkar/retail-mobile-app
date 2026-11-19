import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiService } from './api.service';
import { ConfigService } from './config.service';

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  TestBed.inject(ConfigService).setBaseUrl('http://api.test/api');
  return { api: TestBed.inject(ApiService), ctl: TestBed.inject(HttpTestingController) };
}

describe('ApiService', () => {
  it('get builds the url and leaves out empty parameters', () => {
    const { api, ctl } = setup();
    api.get('/orders', { params: { page: 2, search: null, status: undefined, q: 'milk' } }).subscribe();

    const req = ctl.expectOne(r => r.url === 'http://api.test/api/orders');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('q')).toBe('milk');
    expect(req.request.params.has('search')).toBe(false);
    expect(req.request.params.has('status')).toBe(false);
  });

  it.each([['post'], ['put'], ['patch']] as const)('%s sends the body to the endpoint', method => {
    const { api, ctl } = setup();
    api[method]('/things/1', { a: 1 }).subscribe();
    const req = ctl.expectOne('http://api.test/api/things/1');
    expect(req.request.method).toBe(method.toUpperCase());
    expect(req.request.body).toEqual({ a: 1 });
  });

  it('delete calls the endpoint', () => {
    const { api, ctl } = setup();
    api.delete('/things/1').subscribe();
    expect(ctl.expectOne('http://api.test/api/things/1').request.method).toBe('DELETE');
  });

  const fail = (body: object, status = 400) => {
    const { api, ctl } = setup();
    let error: any;
    api.get('/x').subscribe({ error: e => (error = e) });
    ctl.expectOne('http://api.test/api/x').flush(body, { status, statusText: 'Bad' });
    return error;
  };

  it('an error uses the message the server sent', () => {
    const e = fail({ message: 'Out of stock' });
    expect([e.status, e.message]).toEqual([400, 'Out of stock']);
  });

  it('a list of errors is joined into one message', () => {
    expect(fail({ errors: ['Name is required', 'Price is required'] }).message).toBe('Name is required, Price is required');
  });

  it('with no message the status is shown', () => {
    expect(fail({}, 500).message).toBe('Error: 500 Bad');
  });
});
