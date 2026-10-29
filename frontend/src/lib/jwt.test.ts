import { describe, expect, it } from 'vitest';
import { getUserFromToken } from './jwt';

function makeToken(payload: Record<string, unknown>): string {
  const encoded = btoa(JSON.stringify(payload))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
  return `header.${encoded}.signature`;
}

describe('getUserFromToken', () => {
  it('reads the standard sub, email, name and role claims', () => {
    const user = getUserFromToken(
      makeToken({
        sub: 'user-1',
        email: 'attendee@example.com',
        name: 'Ada Lovelace',
        role: 'Attendee',
      }),
    );

    expect(user).toEqual({
      id: 'user-1',
      email: 'attendee@example.com',
      name: 'Ada Lovelace',
      role: 'Attendee',
    });
  });

  it('supports .NET URI claim names and array roles', () => {
    const user = getUserFromToken(
      makeToken({
        'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier': 'user-2',
        'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress':
          'organizer@example.com',
        'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': [
          'Unknown',
          'Organizer',
        ],
      }),
    );

    expect(user?.id).toBe('user-2');
    expect(user?.email).toBe('organizer@example.com');
    expect(user?.name).toBe('organizer');
    expect(user?.role).toBe('Organizer');
  });

  it('accepts the shorthand role claim when the URI claim is absent', () => {
    const user = getUserFromToken(
      makeToken({
        sub: 'user-3',
        email: 'admin@example.com',
        role: 'Admin',
      }),
    );

    expect(user?.role).toBe('Admin');
  });

  it('rejects malformed tokens, unknown roles and missing role claims', () => {
    expect(getUserFromToken('invalid')).toBeNull();
    expect(getUserFromToken(makeToken({ sub: 'user-4', role: 'Superuser' }))).toBeNull();
    expect(getUserFromToken(makeToken({ sub: 'user-5', email: 'a@b.com' }))).toBeNull();
  });
});