import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  apiRequest,
  createIdempotencyKey,
  errorMessage,
} from '../../api/client';
import type {
  EventDetails,
  MyReservation,
  PaymentResponse,
  Reservation,
} from '../../api/types';
import { ErrorState, Loading } from '../../components/Feedback';
import {
  ArrowIcon,
  CalendarIcon,
  PinIcon,
} from '../../components/Icons';
import { TicketTier } from '../../components/TicketTier';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import {
  formatDateLong,
  formatMoney,
  formatTime,
} from '../../lib/format';
import { useNow } from '../../lib/hooks';

export function EventPage() {
  const { id } = useParams<{ id: string }>();
  const { role, isAuthenticated } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const toast = useToast();
  const now = useNow(1000);

  const [event, setEvent] = useState<EventDetails | null>(null);
  const [reservations, setReservations] = useState<MyReservation[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busyTier, setBusyTier] = useState<string | null>(null);
  const purchaseKeys = useRef(new Map<string, string>());

  const load = useCallback(async () => {
    if (!id) {
      setError('Event not found.');
      setLoading(false);
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const [details, mine] = await Promise.all([
        apiRequest<EventDetails>(`/events/${id}`, { auth: false }),
        role === 'Attendee'
          ? apiRequest<MyReservation[]>('/me/reservations')
          : Promise.resolve([] as MyReservation[]),
      ]);
      setEvent(details);
      setReservations(mine);
    } catch (caught) {
      setError(errorMessage(caught, 'Could not load this event.'));
    } finally {
      setLoading(false);
    }
  }, [id, role]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading) {
    return <Loading label="Loading event" />;
  }

  if (error || !event) {
    return (
      <div className="wrap sec">
        <ErrorState
          message={error ?? 'Event not found.'}
          onRetry={() => void load()}
        />
      </div>
    );
  }

  const eventReservations = reservations.filter(
    (reservation) => reservation.eventId === event.id,
  );
  const active = eventReservations.find(
    (reservation) =>
      reservation.status === 'Paid' ||
      (reservation.status === 'Held' &&
        !reservation.isExpired &&
        new Date(reservation.expiresAt).getTime() > now),
  );
  const hasExpired = eventReservations.some(
    (reservation) => reservation.status === 'Expired' || reservation.isExpired,
  );
  const hasCancelled = eventReservations.some(
    (reservation) => reservation.status === 'Cancelled',
  );
  const eventStarted = new Date(event.startsAt).getTime() <= now;
  const totalRemaining = event.ticketTypes.reduce(
    (total, tier) => total + tier.remaining,
    0,
  );
  const purchaseMode = hasExpired && !active;
  const wrongRole = isAuthenticated && role !== 'Attendee';

  let buyLabel = 'Buy now';
  if (active?.status === 'Paid') {
    buyLabel = 'View ticket';
  } else if (active?.status === 'Held') {
    buyLabel = 'Finish checkout';
  } else if (hasCancelled) {
    buyLabel = 'Reservation closed';
  } else if (eventStarted) {
    buyLabel = 'Event started';
  } else if (!isAuthenticated) {
    buyLabel = 'Sign in to buy';
  } else if (wrongRole) {
    buyLabel = 'Attendee account only';
  }

  const blocked = hasCancelled || wrongRole || (eventStarted && !active);
  const showHold = !purchaseMode && !active && !hasCancelled && !eventStarted && !wrongRole;

  const ensureAttendee = () => {
    if (!isAuthenticated) {
      navigate(`/login?next=${encodeURIComponent(location.pathname)}`);
      return false;
    }

    if (role !== 'Attendee') {
      toast.error('Only attendee accounts can buy tickets.');
      return false;
    }

    if (active?.status === 'Held') {
      navigate(`/checkout/${active.id}`);
      return false;
    }

    if (active?.status === 'Paid') {
      navigate('/my-tickets');
      return false;
    }

    if (hasCancelled || eventStarted) {
      return false;
    }

    return true;
  };

  const buyNow = async (ticketTypeId: string) => {
    if (!ensureAttendee()) {
      return;
    }

    const key = `${event.id}:${ticketTypeId}`;
    let idempotencyKey = purchaseKeys.current.get(key);
    if (!idempotencyKey) {
      idempotencyKey = createIdempotencyKey();
      purchaseKeys.current.set(key, idempotencyKey);
    }

    setBusyTier(ticketTypeId);
    try {
      const payment = await apiRequest<PaymentResponse>(
        `/events/${event.id}/purchase`,
        {
          method: 'POST',
          body: { ticketTypeId },
          headers: { 'Idempotency-Key': idempotencyKey },
        },
      );
      purchaseKeys.current.delete(key);
      toast.success('Ticket issued. See you there.');
      navigate(`/my-tickets?new=${payment.ticketId}`);
    } catch (caught) {
      toast.error(errorMessage(caught));
      void load();
    } finally {
      setBusyTier(null);
    }
  };

  const holdTicket = async (ticketTypeId: string) => {
    if (!ensureAttendee()) {
      return;
    }

    setBusyTier(ticketTypeId);
    try {
      const reservation = await apiRequest<Reservation>(
        `/events/${event.id}/reservations`,
        { method: 'POST', body: { ticketTypeId } },
      );
      toast.success('Ticket held for 10 minutes.');
      navigate(`/checkout/${reservation.id}`);
    } catch (caught) {
      toast.error(errorMessage(caught));
      void load();
    } finally {
      setBusyTier(null);
    }
  };

  return (
    <div className="wrap sec">
      <Link className="backlink" to="/">
        <ArrowIcon size={16} /> All events
      </Link>

      <div className="ev-head">
        <div>
          <span className="sticker">
            {totalRemaining > 0 ? `${totalRemaining} left` : 'Sold out'}
          </span>
          <h1 className="ev-title">{event.title}</h1>
          <dl className="ev-meta">
            <div>
              <dt className="mono">Date</dt>
              <dd>
                <span style={{ display: 'inline-flex', gap: 8, alignItems: 'center' }}>
                  <CalendarIcon /> {formatDateLong(event.startsAt)}
                </span>
                <br />
                <span className="muted">{formatTime(event.startsAt)} WAT</span>
              </dd>
            </div>
            <div>
              <dt className="mono">Venue</dt>
              <dd>
                <span style={{ display: 'inline-flex', gap: 8, alignItems: 'center' }}>
                  <PinIcon /> {event.venue}
                </span>
              </dd>
            </div>
            <div>
              <dt className="mono">Organizer</dt>
              <dd>{event.organizerName || 'EventTicketing'}</dd>
            </div>
            <div>
              <dt className="mono">From</dt>
              <dd>
                {event.ticketTypes.length > 0
                  ? formatMoney(
                      Math.min(...event.ticketTypes.map((tier) => tier.currentPrice)),
                    )
                  : '—'}
              </dd>
            </div>
          </dl>
          <p className="ev-desc">{event.description}</p>
          <div className="cta-row">
            <button
              type="button"
              className="btn"
              disabled={event.ticketTypes.length === 0}
              onClick={() =>
                document
                  .getElementById('tiers')
                  ?.scrollIntoView({ behavior: 'smooth' })
              }
            >
              Choose a ticket <ArrowIcon />
            </button>
            {role === 'Attendee' ? (
              <Link className="btn btn-ghost" to="/my-tickets">
                My tickets
              </Link>
            ) : null}
          </div>
        </div>

        <div
          className="ev-photo ph"
          style={
            event.imageUrl
              ? { backgroundImage: `url("${event.imageUrl}")` }
              : undefined
          }
        >
          {!event.imageUrl ? <span className="ph-label">PHOTO COMING SOON</span> : null}
        </div>
      </div>

      <div id="tiers">
        {active?.status === 'Paid' ? (
          <div className="banner">
            <span>You already have a ticket for this event.</span>
            <Link className="btn btn-sm" to="/my-tickets">
              View ticket
            </Link>
          </div>
        ) : null}

        {active?.status === 'Held' ? (
          <div className="banner">
            <span>
              Your ticket is held until {formatTime(active.expiresAt)}. Finish checkout
              before it expires.
            </span>
            <Link className="btn btn-sm" to={`/checkout/${active.id}`}>
              Finish checkout
            </Link>
          </div>
        ) : null}

        {hasCancelled && !active ? (
          <div className="banner">
            <span>
              Your previous reservation for this event was cancelled, so it can no
              longer be booked.
            </span>
            <Link className="btn btn-sm" to="/">
              Find another event
            </Link>
          </div>
        ) : null}

        {purchaseMode ? (
          <div className="banner">
            <span>
              Your previous hold expired. You can buy a ticket directly below.
            </span>
          </div>
        ) : null}

        {eventStarted ? (
          <div className="banner">
            <span>This event has already started. Ticket sales are closed.</span>
          </div>
        ) : null}

        <div className="tiers-head">
          <h2 className="h2">Pick your ticket</h2>
          <p>One person, one entry. Prices are in Nigerian naira.</p>
        </div>

        {event.ticketTypes.length > 0 ? (
          <div className="tiers">
            {event.ticketTypes.map((tier) => (
              <TicketTier
                key={tier.id}
                tier={tier}
                disabled={blocked}
                busy={busyTier === tier.id}
                buyLabel={buyLabel}
                onBuy={() => void buyNow(tier.id)}
                holdLabel="Hold 10 min"
                onHold={showHold ? () => void holdTicket(tier.id) : undefined}
              />
            ))}
          </div>
        ) : (
          <div className="note">No ticket types have been added to this event yet.</div>
        )}

        <p className="fine">
          Holds last ten minutes. If the timer runs out, the seat goes back on sale.
          {role === 'Attendee' ? ' You can buy directly after an expired hold.' : ''}
        </p>
      </div>
    </div>
  );
}
