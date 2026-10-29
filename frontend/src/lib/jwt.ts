import type { AuthUser, Role } from '../api/types';

interface TokenPayload {
  sub?: string;
  email?: string;
  name?: string;
  role?: string | string[];
  exp?: number;
  [key: string]: unknown;
}

const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';
const NAME_IDENTIFIER_CLAIM =
  'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier';
const EMAIL_CLAIM = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress';

function normalizeRole(value: unknown): Role | null {
  if (Array.isArray(value)) {
    for (const item of value) {
      const role = normalizeRole(item);
      if (role) {
        return role;
      }
    }
    return null;
  }

  if (typeof value !== 'string') {
    return null;
  }

  if (value === 'Admin' || value === 'Organizer' || value === 'Attendee') {
    return value;
  }

  return null;
}

function decodePayload(token: string): TokenPayload | null {
  try {
    const payload = token.split('.')[1];
    if (!payload) {
      return null;
    }

    const normalized = payload.replace(/-/g, '+').replace(/_/g, '/');
    const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '=');
    const json = decodeURIComponent(
      Array.from(atob(padded))
        .map((character) => `%${character.charCodeAt(0).toString(16).padStart(2, '0')}`)
        .join(''),
    );

    return JSON.parse(json) as TokenPayload;
  } catch {
    return null;
  }
}

export function getUserFromToken(token: string): AuthUser | null {
  const payload = decodePayload(token);
  if (!payload) {
    return null;
  }

  const role =
    normalizeRole(payload.role) ??
    normalizeRole(payload[ROLE_CLAIM]) ??
    null;

  if (!role) {
    return null;
  }

  const id =
    (typeof payload.sub === 'string' && payload.sub) ||
    (typeof payload[NAME_IDENTIFIER_CLAIM] === 'string' &&
      (payload[NAME_IDENTIFIER_CLAIM] as string)) ||
    '';

  const email =
    (typeof payload.email === 'string' && payload.email) ||
    (typeof payload[EMAIL_CLAIM] === 'string' && (payload[EMAIL_CLAIM] as string)) ||
    '';

  const name = typeof payload.name === 'string' ? payload.name : email.split('@')[0] ?? '';

  return { id, email, name, role };
}
