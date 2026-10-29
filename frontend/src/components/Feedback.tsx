import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';

export function Loading({ label = 'Loading' }: { label?: string }) {
  return (
    <div className="loading" role="status">
      <span className="spinner" aria-hidden="true" />
      {label}
    </div>
  );
}

export function ErrorState({
  message,
  onRetry,
}: {
  message: string;
  onRetry?: () => void;
}) {
  return (
    <div className="empty" role="alert">
      <p>{message}</p>
      {onRetry ? (
        <button type="button" className="btn btn-ghost" onClick={onRetry}>
          Try again
        </button>
      ) : null}
    </div>
  );
}

export function EmptyState({
  title,
  message,
  actionLabel,
  actionTo,
  children,
}: {
  title: string;
  message: string;
  actionLabel?: string;
  actionTo?: string;
  children?: ReactNode;
}) {
  return (
    <div className="empty">
      <h3 className="h3">{title}</h3>
      <p>{message}</p>
      {actionLabel && actionTo ? (
        <Link className="btn" to={actionTo}>
          {actionLabel}
        </Link>
      ) : null}
      {children}
    </div>
  );
}
