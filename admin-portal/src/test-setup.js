// Newer Node versions ship their own localStorage that throws without a file path and hides jsdom's; tests use a plain in-memory one.
class MemoryStorage {
  data = new Map();
  get length() { return this.data.size; }
  clear() { this.data.clear(); }
  getItem(key) { return this.data.get(key) ?? null; }
  key(index) { return [...this.data.keys()][index] ?? null; }
  removeItem(key) { this.data.delete(key); }
  setItem(key, value) { this.data.set(key, String(value)); }
}

Object.defineProperty(globalThis, 'localStorage', { value: new MemoryStorage(), configurable: true });
Object.defineProperty(window, 'localStorage', { value: globalThis.localStorage, configurable: true });
