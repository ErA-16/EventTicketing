import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, vi } from 'vitest';
import { clearSession } from '../api/client';

afterEach(() => {
  cleanup();
  clearSession();
  localStorage.clear();
  vi.restoreAllMocks();
});