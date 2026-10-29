import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { flushSync } from 'react-dom';
import { useLocation, useNavigate } from 'react-router-dom';
import {
  apiRequest,
  getSession,
  logoutRequest,
  setSessionFromAuth,
  subscribeAuthEvents,
  subscribeSession,
  type StoredSession,
} from '../api/client';
import type { AuthResponse, AuthUser, Role } from '../api/types';
import { getUserFromToken } from '../lib/jwt';
import { useToast } from './ToastContext';

interface LoginInput {
  email: string;
  password: string;
}

interface RegisterInput extends LoginInput {
  firstName: string;
  lastName: string;
  organizationName: string;
  isOrganizer: boolean;
}

interface AuthApi {
  session: StoredSession | null;
  user: AuthUser | null;
  role: Role | null;
  isAuthenticated: boolean;
  login: (input: LoginInput) => Promise<AuthUser>;
  register: (input: RegisterInput) => Promise<AuthUser>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthApi | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<StoredSession | null>(() => getSession());
  const navigate = useNavigate();
  const location = useLocation();
  const toast = useToast();

  useEffect(() => subscribeSession(setSession), []);

  useEffect(
    () =>
      subscribeAuthEvents((event) => {
        toast.error(event.message);
        if (!location.pathname.startsWith('/login')) {
          const next = `${location.pathname}${location.search}`;
          navigate(`/login?next=${encodeURIComponent(next)}`, { replace: true });
        }
      }),
    [location.pathname, location.search, navigate, toast],
  );

  const user = useMemo(
    () => (session ? getUserFromToken(session.accessToken) : null),
    [session],
  );

  const login = useCallback(async (input: LoginInput) => {
    const response = await apiRequest<AuthResponse>('/auth/login', {
      method: 'POST',
      body: { email: input.email.trim(), password: input.password },
      auth: false,
    });

    setSessionFromAuth(response);
    const nextUser = getUserFromToken(response.token);
    if (!nextUser) {
      throw new Error('Sign-in failed. Please try again.');
    }

    return nextUser;
  }, []);

  const register = useCallback(
    async (input: RegisterInput) => {
      await apiRequest<unknown>('/auth/register', {
        method: 'POST',
        body: {
          firstName: input.firstName.trim(),
          lastName: input.lastName.trim(),
          organizationName: input.organizationName.trim(),
          email: input.email.trim(),
          password: input.password,
          isOrganizer: input.isOrganizer,
        },
        auth: false,
      });

      return login({ email: input.email, password: input.password });
    },
    [login],
  );

  const logout = useCallback(async () => {
    flushSync(() => {
      navigate('/', { replace: true });
    });
    await logoutRequest();
    toast.success('You have been logged out.');
  }, [navigate, toast]);

  const value = useMemo<AuthApi>(
    () => ({
      session,
      user,
      role: user?.role ?? null,
      isAuthenticated: Boolean(session && user),
      login,
      register,
      logout,
    }),
    [login, logout, register, session, user],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthApi {
  const value = useContext(AuthContext);
  if (!value) {
    throw new Error('useAuth must be used inside AuthProvider.');
  }
  return value;
}
