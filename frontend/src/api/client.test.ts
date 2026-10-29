import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  ApiError,
  apiRequest,
  clearSession,
  errorMessage,
  getSession,
  logoutRequest,
  setSessionFromAuth,
} from './client';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('apiRequest', () => {
  beforeEach(() => {
    clearSession();
    vi.unstubAllGlobals();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('calls public endpoints without an Authorization header', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ items: [] }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await apiRequest<{ items: unknown[] }>('/events', { auth: false });

    expect(result).toEqual({ items: [] });
    expect(fetchMock).toHaveBeenCalledOnce();
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('http://127.0.0.1:5276/api/v1/events');
    expect(new Headers(init.headers).get('Authorization')).toBeNull();
    expect(init.method).toBe('GET');
  });

  it('adds the bearer token and serializes JSON bodies', async () => {
    setSessionFromAuth({
      token: 'access-token',
      refreshToken: 'refresh-token',
      expiresIn: 300,
    });
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }));
    vi.stubGlobal('fetch', fetchMock);

    await apiRequest('/wallet/fund', { method: 'POST', body: { amount: 5000 } });

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('http://127.0.0.1:5276/api/v1/wallet/fund');
    expect(init.method).toBe('POST');
    expect(new Headers(init.headers).get('Authorization')).toBe('Bearer access-token');
    expect(new Headers(init.headers).get('Content-Type')).toBe('application/json');
    expect(init.body).toBe('{"amount":5000}');
  });

  it('returns undefined for a 204 response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
    await expect(apiRequest('/reservations/1', { method: 'DELETE', auth: false })).resolves.toBeUndefined();
  });

  it('maps API errors to ApiError with code and details', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse(
          {
            code: 'validation_failed',
            message: 'One or more fields are invalid.',
            details: ['Email is required.'],
          },
          400,
        ),
      ),
    );

    await expect(apiRequest('/auth/register', { auth: false })).rejects.toMatchObject({
      status: 400,
      code: 'validation_failed',
      message: 'One or more fields are invalid.',
      details: ['Email is required.'],
    });
  });

  it('uses a friendly message for rate limits', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse({ code: 'too_many_requests', message: 'slow down' }, 429),
      ),
    );

    await expect(apiRequest('/auth/login', { auth: false })).rejects.toMatchObject({
      status: 429,
      code: 'too_many_requests',
      message: 'Too many attempts. Wait a minute and try again.',
    });
  });

  it('maps network failures to a user-safe message', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('fetch failed')));

    await expect(apiRequest('/events', { auth: false })).rejects.toMatchObject({
      status: 0,
      code: 'network_error',
      message: "We couldn't connect right now. Please try again in a moment.",
    });
  });

  it('hides server details behind a generic message', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse(
          {
            code: 'internal_error',
            message: 'Npgsql connection failed.',
            details: ['Connection string rejected.'],
          },
          500,
        ),
      ),
    );

    await expect(apiRequest('/events', { auth: false })).rejects.toMatchObject({
      status: 500,
      code: 'internal_error',
      message: 'Something went wrong. Please try again.',
      details: [],
    });
  });

  it('refreshes once when the access token is close to expiry', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-27T00:00:00Z'));
    setSessionFromAuth({
      token: 'old-access',
      refreshToken: 'refresh-token',
      expiresIn: 10,
    });

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ token: 'new-access', expiresIn: 300 }))
      .mockResolvedValueOnce(jsonResponse({ value: 1 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(apiRequest('/private', { auth: true })).resolves.toEqual({ value: 1 });

    expect(fetchMock).toHaveBeenCalledTimes(2);
    const [refreshUrl, refreshInit] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(refreshUrl).toBe('http://127.0.0.1:5276/api/v1/auth/refresh');
    expect(refreshInit.method).toBe('POST');
    expect(refreshInit.body).toBe(JSON.stringify({ refreshToken: 'refresh-token' }));

    const [, privateInit] = fetchMock.mock.calls[1] as [string, RequestInit];
    expect(new Headers(privateInit.headers).get('Authorization')).toBe('Bearer new-access');
    expect(getSession()).toMatchObject({
      accessToken: 'new-access',
      refreshToken: 'refresh-token',
    });
  });

  it('clears the session when refresh fails', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-27T00:00:00Z'));
    setSessionFromAuth({
      token: 'old-access',
      refreshToken: 'refresh-token',
      expiresIn: 10,
    });

    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse({ code: 'refresh_failed', message: 'Session expired.' }, 401),
      ),
    );

    await expect(apiRequest('/private', { auth: true })).rejects.toMatchObject({
      status: 401,
      code: 'refresh_failed',
    });
    expect(getSession()).toBeNull();
  });
});

describe('errorMessage', () => {
  it('prefers short validation details over a generic message', () => {
    const error = new ApiError(400, 'validation_failed', 'Validation failed.', [
      'Password is too weak.',
    ]);
    expect(errorMessage(error)).toBe('Password is too weak.');
  });

  it('falls back to the API message and then a supplied default', () => {
    expect(errorMessage(new ApiError(409, 'conflict', 'Already reserved.'))).toBe(
      'Already reserved.',
    );
    expect(errorMessage(new Error('Network down.'))).toBe('Something went wrong.');
    expect(
      errorMessage(new Error('Database failed.'), 'Could not load events.'),
    ).toBe('Could not load events.');
    expect(errorMessage('unknown', 'Default message.')).toBe('Default message.');
  });
});

describe('logoutRequest', () => {
  it('clears the local session even when the API call fails', async () => {
    setSessionFromAuth({
      token: 'access-token',
      refreshToken: 'refresh-token',
      expiresIn: 300,
    });
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')));

    await logoutRequest();

    expect(getSession()).toBeNull();
  });
});