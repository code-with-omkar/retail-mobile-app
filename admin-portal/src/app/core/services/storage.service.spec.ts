import { TestBed } from '@angular/core/testing';
import { StorageService } from './storage.service';

describe('StorageService', () => {
  let storage: StorageService;

  beforeEach(() => {
    localStorage.clear();
    storage = TestBed.inject(StorageService);
  });

  it('keeps and returns the token and refresh token', () => {
    storage.setToken('a');
    storage.setRefreshToken('r');
    expect([storage.getToken(), storage.getRefreshToken()]).toEqual(['a', 'r']);
  });

  it('returns null when nothing is stored', () => {
    expect([storage.getToken(), storage.getRefreshToken(), storage.getUserContext()]).toEqual([null, null, null]);
  });

  it('round-trips the user context as JSON', () => {
    storage.setUserContext({ userId: 'u1', role: 'Admin' });
    expect(storage.getUserContext()).toEqual({ userId: 'u1', role: 'Admin' });
  });

  it('clear removes the token, refresh token and user context', () => {
    storage.setToken('a');
    storage.setRefreshToken('r');
    storage.setUserContext({ userId: 'u1' });
    storage.clear();
    expect([storage.getToken(), storage.getRefreshToken(), storage.getUserContext()]).toEqual([null, null, null]);
  });
});
