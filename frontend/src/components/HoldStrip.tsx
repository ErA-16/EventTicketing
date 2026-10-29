import { useEffect, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { apiRequest } from '../api/client';
import type { MyReservation } from '../api/types';
import { useAuth } from '../context/AuthContext';
import { formatHold } from '../lib/format';
import { useNow } from '../lib/hooks';

export function HoldStrip() {
  const { role, isAuthenticated } = useAuth();
  const location = useLocation();
  const now = useNow(1000);
  const [hold, setHold] = useState<MyReservation | null>(null);

  useEffect(() => {
    if (!isAuthenticated || role !== 'Attendee') {
      setHold(null);
      return;
    }

    let active = true;
    apiRequest<MyReservation[]>('/me/reservations')
      .then((reservations) => {
        if (!active) {
          return;
        }
        const next = reservations
          .filter(
            (reservation) =>
              reservation.status === 'Held' &&
              !reservation.isExpired &&
              new Date(reservation.expiresAt).getTime() > Date.now(),
          )
          .sort(
            (a, b) =>
              new Date(a.expiresAt).getTime() - new Date(b.expiresAt).getTime(),
          )[0];
        setHold(next ?? null);
      })
      .catch(() => {
        if (active) {
          setHold(null);
        }
      });

    return () => {
      active = false;
    };
  }, [isAuthenticated, role, location.pathname]);

  if (!hold || location.pathname.startsWith('/checkout')) {
    return null;
  }

  const remaining = new Date(hold.expiresAt).getTime() - now;
  if (remaining <= 0) {
    return null;
  }

  return (
    <div className="holdstrip" role="status">
      <div className="wrap">
        <span>
          Ticket held · {hold.eventTitle} ·{' '}
          <span className="mono">{formatHold(remaining)}</span>
        </span>
        <Link className="btn" to={`/checkout/${hold.id}`}>
          Finish checkout
        </Link>
      </div>
    </div>
  );
}
