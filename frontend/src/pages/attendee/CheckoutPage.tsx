import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  apiRequest,
  createIdempotencyKey,
  errorMessage,
  isApiError,
} from '../../api/client';
import type {
  EventDetails,
  MyReservation,
  PaymentResponse,
  Wallet,
} from '../../api/types';
import { ErrorState, Loading } from '../../components/Feedback';
import { ArrowIcon, WalletIcon } from '../../components/Icons';
import { Ticket } from '../../components/Ticket';
import { useToast } from '../../context/ToastContext';
import {
  formatDateTimeLong,
  formatHold,
  formatMoney,
} from '../../lib/format';
import { useNow } from '../../lib/hooks';

export function CheckoutPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const toast = useToast();
  const now = useNow(1000);
  const idempotencyKey = useRef(createIdempotencyKey());

  const [reservation, setReservation] = useState<MyReservation | null>(null);
  const [event, setEvent] = useState<EventDetails | null>(null);
  const [wallet, setWallet] = useState<Wallet | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [paying, setPaying] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [funding, setFunding] = useState<number | null>(null);

  const load = useCallback(async () => {
    if (!id) {
      setError('Reservation not found.');
      setLoading(false);
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const [reservations, walletResponse] = await Promise.all([
        apiRequest<MyReservation[]>('/me/reservations'),
        apiRequest<Wallet>('/wallet'),
      ]);
      const found = reservations.find((item) => item.id === id);
      if (!found) {
        setError('This reservation could not be found.');
        return;
      }

      const details = await apiRequest<EventDetails>(`/events/${found.eventId}`, {
        auth: false,
      });
      setReservation(found);
      setEvent(details);
      setWallet(walletResponse);
    } catch (caught) {
      setError(errorMessage(caught, 'Could not load this reservation.'));
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading) {
    return <Loading label="Loading checkout" />;
  }

  if (error || !reservation || !event) {
    return (
      <div className="wrap sec">
        <ErrorState
          message={error ?? 'This reservation could not be found.'}
          onRetry={() => void load()}
        />
        <div style={{ textAlign: 'center', marginTop: 16 }}>
          <Link className="btn btn-ghost" to="/">
            Back to events
          </Link>
        </div>
      </div>
    );
  }

  const remainingMs = new Date(reservation.expiresAt).getTime() - now;
  const expired =
    reservation.status === 'Expired' ||
    reservation.status === 'Cancelled' ||
    (reservation.status === 'Held' && remainingMs <= 0);
  const paid = reservation.status === 'Paid';
  const low = remainingMs < 2 * 60 * 1000;
  const totalHold = Math.max(
    1,
    new Date(reservation.expiresAt).getTime() - new Date(reservation.createdAt).getTime(),
  );
  const holdPercent = Math.max(0, Math.min(100, (remainingMs / totalHold) * 100));
  const balance = wallet?.balance ?? 0;
  const canPay = balance >= reservation.priceAtReservation;

  const pay = async () => {
    if (expired || paid) {
      return;
    }

    setPaying(true);
    try {
      const payment = await apiRequest<PaymentResponse>(
        `/reservations/${reservation.id}/payment`,
        {
          method: 'POST',
          headers: { 'Idempotency-Key': idempotencyKey.current },
        },
      );
      toast.success('Payment complete. Your ticket is ready.');
      navigate(`/my-tickets?new=${payment.ticketId}`);
    } catch (caught) {
      toast.error(errorMessage(caught));
      if (isApiError(caught) && caught.status === 410) {
        setReservation((current) =>
          current ? { ...current, status: 'Expired', isExpired: true } : current,
        );
      } else {
        void load();
      }
    } finally {
      setPaying(false);
    }
  };

  const cancel = async () => {
    if (paid) {
      return;
    }

    setCancelling(true);
    try {
      await apiRequest<void>(`/reservations/${reservation.id}`, { method: 'DELETE' });
      toast.success('Reservation cancelled.');
      navigate(`/events/${reservation.eventId}`);
    } catch (caught) {
      toast.error(errorMessage(caught));
    } finally {
      setCancelling(false);
    }
  };

  const fund = async (amount: number) => {
    setFunding(amount);
    try {
      const updated = await apiRequest<Wallet>('/wallet/fund', {
        method: 'POST',
        body: { amount },
      });
      setWallet(updated);
      toast.success(`${formatMoney(amount)} added to your wallet.`);
    } catch (caught) {
      toast.error(errorMessage(caught));
    } finally {
      setFunding(null);
    }
  };

  return (
    <div className="wrap sec">
      <Link className="backlink" to={`/events/${reservation.eventId}`}>
        <ArrowIcon size={16} /> Back to event
      </Link>

      <div className="page-head">
        <div>
          <span className="mono muted" style={{ fontSize: '.7rem' }}>
            Checkout
          </span>
          <h1 className="h2">Almost yours.</h1>
        </div>
        <span className={`status ${paid ? 's-approved' : expired ? 's-rejected' : 's-pending'}`}>
          {paid ? 'Paid' : expired ? 'Expired' : 'Held'}
        </span>
      </div>

      <div className="co">
        <div>
          <div className="preview">
            {!paid && !expired ? <span className="sticker ink">Unpaid</span> : null}
            <Ticket
              title={event.title}
              venue={event.venue}
              startsAt={event.startsAt}
              organizerName={event.organizerName}
              ticketTypeName={reservation.ticketTypeName}
              pricePaid={reservation.priceAtReservation}
              codePlaceholder={paid ? 'PAID' : expired ? 'EXPIRED' : 'PENDING'}
              tone="orange"
            />
          </div>

          <div className="panel" style={{ marginTop: 24 }}>
            <div className="panel-head">
              <h2 className="h3">Order summary</h2>
              <span className="status s-draft">{reservation.ticketTypeName}</span>
            </div>
            <dl className="sum">
              <div>
                <dt>Event</dt>
                <dd>{event.title}</dd>
              </div>
              <div>
                <dt>When</dt>
                <dd>{formatDateTimeLong(event.startsAt)}</dd>
              </div>
              <div>
                <dt>Venue</dt>
                <dd>{event.venue}</dd>
              </div>
              <div>
                <dt>Ticket</dt>
                <dd>{reservation.ticketTypeName}</dd>
              </div>
              <div>
                <dt>Total</dt>
                <dd>{formatMoney(reservation.priceAtReservation)}</dd>
              </div>
            </dl>
            <p className="fine">
              Prices are locked when you reserve. Payment is taken from your demo
              wallet.
            </p>
          </div>
        </div>

        <aside className="panel">
          {paid ? (
            <>
              <h2 className="h3">Ticket issued</h2>
              <p className="muted" style={{ marginTop: 8 }}>
                Payment for this reservation is complete.
              </p>
              <Link
                className="btn btn-lime btn-block"
                style={{ marginTop: 18 }}
                to="/my-tickets"
              >
                View my ticket <ArrowIcon />
              </Link>
            </>
          ) : expired ? (
            <>
              <h2 className="h3">This hold has ended</h2>
              <p className="muted" style={{ marginTop: 8 }}>
                The seat went back on sale. If this is your first expired hold, you
                can buy the ticket directly from the event page.
              </p>
              <Link
                className="btn btn-block"
                style={{ marginTop: 18 }}
                to={`/events/${reservation.eventId}`}
              >
                Back to event <ArrowIcon />
              </Link>
            </>
          ) : (
            <>
              <div className="hold-label mono">
                {low ? 'Hurry — hold expiring' : 'Your hold expires in'}
              </div>
              <div className={`hold-time${low ? ' low' : ''}`} aria-live="polite">
                {formatHold(remainingMs)}
              </div>
              <div className={`hold-bar${low ? ' low' : ''}`}>
                <i style={{ width: `${holdPercent}%` }} />
              </div>

              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  gap: 12,
                  marginBottom: 18,
                }}
              >
                <span className="mono muted" style={{ fontSize: '.66rem' }}>
                  Wallet balance
                </span>
                <span>
                  <WalletIcon /> {formatMoney(balance)}
                </span>
              </div>

              <button
                type="button"
                className="btn btn-lime btn-block"
                disabled={!canPay || paying}
                onClick={() => void pay()}
              >
                {paying ? 'Paying…' : `Pay ${formatMoney(reservation.priceAtReservation)}`}
              </button>

              {!canPay ? (
                <>
                  <p className="fine">
                    You need {formatMoney(reservation.priceAtReservation - balance)}{' '}
                    more. Top up below, then pay with the same hold.
                  </p>
                  <div className="fund">
                    <button
                      type="button"
                      className="pill"
                      disabled={funding !== null}
                      onClick={() => void fund(5000)}
                    >
                      {funding === 5000 ? 'Adding…' : '+ ₦5,000'}
                    </button>
                    <button
                      type="button"
                      className="pill"
                      disabled={funding !== null}
                      onClick={() => void fund(10000)}
                    >
                      {funding === 10000 ? 'Adding…' : '+ ₦10,000'}
                    </button>
                  </div>
                </>
              ) : null}

              <button
                type="button"
                className="btn btn-ghost btn-block"
                style={{ marginTop: 10 }}
                disabled={cancelling}
                onClick={() => void cancel()}
              >
                {cancelling ? 'Cancelling…' : 'Cancel reservation'}
              </button>
              <p className="fine">
                Cancelling releases the seat immediately. You cannot reserve this
                event again after a cancellation.
              </p>
            </>
          )}
        </aside>
      </div>
    </div>
  );
}
