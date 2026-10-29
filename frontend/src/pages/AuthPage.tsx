import { useRef, useState, type FormEvent } from 'react';
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import { errorMessage, isApiError } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { roleHome } from '../lib/format';

export function AuthPage({ mode }: { mode: 'login' | 'signup' }) {
  const { login, register, isAuthenticated, role } = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const toast = useToast();

  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [organizationName, setOrganizationName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [isOrganizer, setIsOrganizer] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [details, setDetails] = useState<string[]>([]);
  const [confirmError, setConfirmError] = useState<string | null>(null);
  const confirmRef = useRef<HTMLInputElement>(null);

  const next = searchParams.get('next');
  const safeNext = next && next.startsWith('/') ? next : null;

  if (isAuthenticated) {
    return <Navigate to={safeNext ?? roleHome(role)} replace />;
  }

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    setDetails([]);
    setConfirmError(null);

    if (mode === 'signup' && password !== confirmPassword) {
      setConfirmError('Passwords do not match.');
      confirmRef.current?.focus();
      confirmRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' });
      return;
    }

    setSubmitting(true);
    try {
      const user =
        mode === 'login'
          ? await login({ email, password })
          : await register({
              firstName: isOrganizer ? '' : firstName,
              lastName: isOrganizer ? '' : lastName,
              organizationName: isOrganizer ? organizationName : '',
              email,
              password,
              isOrganizer,
            });

      toast.success(mode === 'login' ? 'Welcome back.' : 'Account created.');
      navigate(safeNext ?? roleHome(user.role), { replace: true });
    } catch (caught) {
      setError(errorMessage(caught));
      if (isApiError(caught)) {
        setDetails(caught.details);
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="wrap sec">
      <div className="auth panel">
        <span className="mono muted" style={{ fontSize: '.7rem' }}>
          {mode === 'login' ? 'Welcome back' : 'Join the list'}
        </span>
        <h1 className="h2" style={{ marginTop: 10 }}>
          {mode === 'login' ? 'Log in' : 'Create account'}
        </h1>
        <p className="muted" style={{ marginTop: 10 }}>
          {mode === 'login'
            ? 'Your tickets and wallet are waiting.'
            : 'Buy tickets, hold seats, and keep every code in one place.'}
        </p>

        <form onSubmit={handleSubmit}>
          <div className="form-grid">
            {mode === 'signup' ? (
              <label className="check full">
                <input
                  type="checkbox"
                  checked={isOrganizer}
                  onChange={(inputEvent) => setIsOrganizer(inputEvent.target.checked)}
                />
                I&apos;m creating events as an organizer
              </label>
            ) : null}

            {mode === 'signup' && !isOrganizer ? (
              <>
                <div className="field">
                  <label htmlFor="firstName">First name</label>
                  <input
                    id="firstName"
                    type="text"
                    value={firstName}
                    onChange={(inputEvent) => setFirstName(inputEvent.target.value)}
                    autoComplete="given-name"
                    maxLength={100}
                    required
                  />
                </div>
                <div className="field">
                  <label htmlFor="lastName">Last name</label>
                  <input
                    id="lastName"
                    type="text"
                    value={lastName}
                    onChange={(inputEvent) => setLastName(inputEvent.target.value)}
                    autoComplete="family-name"
                    maxLength={100}
                    required
                  />
                </div>
              </>
            ) : null}

            {mode === 'signup' && isOrganizer ? (
              <div className="field full">
                <label htmlFor="organizationName">Organization name</label>
                <input
                  id="organizationName"
                  type="text"
                  value={organizationName}
                  onChange={(inputEvent) => setOrganizationName(inputEvent.target.value)}
                  autoComplete="organization"
                  maxLength={200}
                  placeholder="e.g. Demo Nights"
                  required
                />
                <span className="hint">This is the public name shown on your events.</span>
              </div>
            ) : null}

            <div className="field full">
              <label htmlFor="email">Email</label>
              <input
                id="email"
                type="email"
                value={email}
                onChange={(inputEvent) => setEmail(inputEvent.target.value)}
                autoComplete="email"
                required
              />
            </div>

            <div className="field full">
              <label htmlFor="password">Password</label>
                <input
                  id="password"
                  type="password"
                  value={password}
                  onChange={(inputEvent) => {
                    setPassword(inputEvent.target.value);
                    setConfirmError(null);
                  }}
                  autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
                  minLength={mode === 'signup' ? 8 : undefined}
                  required
                />
              {mode === 'signup' ? (
                <span className="hint">
                  8+ characters with uppercase, lowercase, a number and a symbol.
                </span>
              ) : null}
            </div>

            {mode === 'signup' ? (
              <>
                <div className="field full">
                  <label htmlFor="confirmPassword">Confirm password</label>
                  <input
                    id="confirmPassword"
                    ref={confirmRef}
                    type="password"
                    value={confirmPassword}
                    onChange={(inputEvent) => {
                      setConfirmPassword(inputEvent.target.value);
                      setConfirmError(null);
                    }}
                    autoComplete="new-password"
                    aria-invalid={confirmError ? true : undefined}
                    aria-describedby={
                      confirmError ? 'confirm-password-error' : undefined
                    }
                    required
                  />
                  {confirmError ? (
                    <span
                      id="confirm-password-error"
                      className="field-error"
                      role="alert"
                    >
                      {confirmError}
                    </span>
                  ) : null}
                </div>
              </>
            ) : null}
          </div>

          {error ? (
            <div className="alert" role="alert" style={{ marginTop: 20 }}>
              {error}
              {details.length > 0 ? (
                <ul>
                  {details.map((detail) => (
                    <li key={detail}>{detail}</li>
                  ))}
                </ul>
              ) : null}
            </div>
          ) : null}

          <button type="submit" className="btn btn-block" disabled={submitting}>
            {submitting
              ? 'Working…'
              : mode === 'login'
                ? 'Log in'
                : 'Create account'}
          </button>
        </form>

        <p className="muted" style={{ marginTop: 18, fontSize: '.9rem' }}>
          {mode === 'login' ? (
            <>
              New here? <Link to="/signup">Create an account</Link>
            </>
          ) : (
            <>
              Already have an account? <Link to="/login">Log in</Link>
            </>
          )}
        </p>
      </div>
    </div>
  );
}
