import type { AuthResponse, RefreshResponse } from './types';

const API_BASE_URL = (
  (import.meta.env.VITE_API_BASE_URL as string | undefined) ??
  'http://127.0.0.1:5276/api/v1'
).replace(/\/+$/, '');

const SESSION_KEY = 'eventticketing.session.v1';
const NETWORK_ERROR_MESSAGE =
  "We couldn't connect right now. Please try again in a moment.";
const SERVER_ERROR_MESSAGE = 'Something went wrong. Please try again.';

export interface StoredSession {
  accessToken: string;
  refreshToken: string;
  expiresAt: number;
}

type SessionListener = (session: StoredSession | null) => void;

function logForDevelopers(error: unknown): void {
  if (import.meta.env.DEV && import.meta.env.MODE !== 'test') {
    console.error(error);
  }
}

export interface AuthEvent {
  type: 'expired';
  message: string;
}

type AuthEventListener = (event: AuthEvent) => void;

const sessionListeners = new Set<SessionListener>();
const authEventListeners = new Set<AuthEventListener>();

let session: StoredSession | null = readStoredSession();
let refreshPromise: Promise<string> | null = null;

function readStoredSession(): StoredSession | null {
  try {
    const raw = localStorage.getItem(SESSION_KEY);
    if (!raw) {
      return null;
    }

    const parsed = JSON.parse(raw) as Partial<StoredSession>;
    if (
      typeof parsed.accessToken !== 'string' ||
      typeof parsed.refreshToken !== 'string' ||
      typeof parsed.expiresAt !== 'number'
    ) {
      return null;
    }

    return {
      accessToken: parsed.accessToken,
      refreshToken: parsed.refreshToken,
      expiresAt: parsed.expiresAt,
    };
  } catch {
    return null;
  }
}

function persistSession(next: StoredSession | null): void {
  try {
    if (next) {
      localStorage.setItem(SESSION_KEY, JSON.stringify(next));
    } else {
      localStorage.removeItem(SESSION_KEY);
    }
  } catch {
    // Storage can be disabled; the in-memory session still works for this tab.
  }
}

function notifySession(): void {
  for (const listener of sessionListeners) {
    listener(session);
  }
}

function notifyAuthEvent(event: AuthEvent): void {
  for (const listener of authEventListeners) {
    listener(event);
  }
}

export function getSession(): StoredSession | null {
  return session;
}

export function subscribeSession(listener: SessionListener): () => void {
  sessionListeners.add(listener);
  return () => {
    sessionListeners.delete(listener);
  };
}

export function subscribeAuthEvents(listener: AuthEventListener): () => void {
  authEventListeners.add(listener);
  return () => {
    authEventListeners.delete(listener);
  };
}

export function setSessionFromAuth(response: AuthResponse): StoredSession {
  session = {
    accessToken: response.token,
    refreshToken: response.refreshToken,
    expiresAt: Date.now() + response.expiresIn * 1000,
  };
  persistSession(session);
  notifySession();
  return session;
}

export function clearSession(): void {
  session = null;
  persistSession(null);
  notifySession();
}

export class ApiError extends Error {
  status: number;
  code: string;
  details: string[];

  constructor(status: number, code: string, message: string, details: string[] = []) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.details = details;
  }
}

interface ErrorBody {
  code?: string;
  message?: string;
  details?: string[] | string | null;
}

async function readResponseBody(response: Response): Promise<unknown> {
  if (response.status === 204) {
    return undefined;
  }

  const text = await response.text();
  if (!text) {
    return undefined;
  }

  try {
    return JSON.parse(text) as unknown;
  } catch {
    return text;
  }
}

function toApiError(response: Response, body: unknown): ApiError {
  const data =
    body && typeof body === 'object' ? (body as ErrorBody) : ({} as ErrorBody);
  const isServerError = response.status >= 500;
  const details = isServerError
    ? []
    : Array.isArray(data.details)
      ? data.details.filter((detail): detail is string => typeof detail === 'string')
      : typeof data.details === 'string'
        ? [data.details]
        : [];

  let message =
    typeof data.message === 'string' && data.message.trim()
      ? data.message
      : `Request failed (${response.status}).`;

  if (isServerError) {
    message = SERVER_ERROR_MESSAGE;
  } else if (response.status === 429) {
    message = 'Too many attempts. Wait a minute and try again.';
  }

  return new ApiError(
    response.status,
    typeof data.code === 'string' ? data.code : 'request_failed',
    message,
    details,
  );
}

export function errorMessage(error: unknown, fallback = 'Something went wrong.'): string {
  if (error instanceof ApiError) {
    if (
      error.details.length > 0 &&
      error.message.length < 60 &&
      !error.details.includes(error.message)
    ) {
      return error.details.join(' ');
    }
    return error.message;
  }

  if (error instanceof Error) {
    logForDevelopers(error);
    return fallback;
  }

  return fallback;
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

async function refreshAccessToken(): Promise<string> {
  const current = session;
  if (!current?.refreshToken) {
    throw new ApiError(401, 'not_authenticated', 'Please log in.');
  }

  if (refreshPromise) {
    return refreshPromise;
  }

  refreshPromise = (async () => {
    let response: Response;
    try {
      response = await fetch(`${API_BASE_URL}/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ refreshToken: current.refreshToken }),
      });
    } catch (caught) {
      logForDevelopers(caught);
      throw new ApiError(0, 'network_error', NETWORK_ERROR_MESSAGE);
    }

    const body = await readResponseBody(response);
    if (!response.ok) {
      const error = toApiError(response, body);
      clearSession();
      notifyAuthEvent({ type: 'expired', message: error.message });
      throw error;
    }

    const data = body as RefreshResponse;
    session = {
      accessToken: data.token,
      refreshToken: current.refreshToken,
      expiresAt: Date.now() + data.expiresIn * 1000,
    };
    persistSession(session);
    notifySession();
    return data.token;
  })().finally(() => {
    refreshPromise = null;
  });

  return refreshPromise;
}

async function getAccessToken(): Promise<string> {
  const current = session;
  if (!current) {
    throw new ApiError(401, 'not_authenticated', 'Please log in to continue.');
  }

  if (Date.now() < current.expiresAt - 15_000) {
    return current.accessToken;
  }

  return refreshAccessToken();
}

export interface ApiRequestOptions {
  method?: string;
  body?: unknown;
  auth?: boolean;
  headers?: HeadersInit;
}

export async function apiRequest<T>(
  path: string,
  options: ApiRequestOptions = {},
): Promise<T> {
  const auth = options.auth ?? true;
  const method = options.method ?? (options.body === undefined ? 'GET' : 'POST');

  const send = async (isRetry: boolean): Promise<T> => {
    const headers = new Headers(options.headers);
    headers.set('Accept', 'application/json');

    if (auth) {
      const token = await getAccessToken();
      headers.set('Authorization', `Bearer ${token}`);
    }

    let body: BodyInit | undefined;
    if (options.body instanceof FormData) {
      body = options.body;
    } else if (options.body !== undefined) {
      headers.set('Content-Type', 'application/json');
      body = JSON.stringify(options.body);
    }

    let response: Response;
    try {
      response = await fetch(`${API_BASE_URL}${path}`, {
        method,
        headers,
        body,
      });
    } catch (caught) {
      logForDevelopers(caught);
      throw new ApiError(0, 'network_error', NETWORK_ERROR_MESSAGE);
    }

    if (response.status === 401 && auth && !isRetry && session?.refreshToken) {
      await refreshAccessToken();
      return send(true);
    }

    const data = await readResponseBody(response);
    if (!response.ok) {
      throw toApiError(response, data);
    }

    return data as T;
  };

  return send(false);
}

export async function logoutRequest(): Promise<void> {
  const current = session;
  if (!current?.refreshToken) {
    clearSession();
    return;
  }

  try {
    await fetch(`${API_BASE_URL}/auth/logout`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ refreshToken: current.refreshToken }),
    });
  } catch {
    // Logout locally even when the network is unavailable.
  } finally {
    clearSession();
  }
}

export function createIdempotencyKey(): string {
  if (typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  return `web-${Date.now()}-${Math.random().toString(36).slice(2, 12)}`;
}

export { API_BASE_URL };
