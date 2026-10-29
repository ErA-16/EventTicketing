import { useCallback, useEffect, useState, type CSSProperties } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { apiRequest, errorMessage } from '../../api/client';
import type { CursorPage, MyReservation, MyTicket } from '../../api/types';
import { EmptyState, ErrorState, Loading } from '../../components/Feedback';
import { CopyIcon } from '../../components/Icons';
import { Ticket, TicketBack, type TicketTone } from '../../components/Ticket';
import { useToast } from '../../context/ToastContext';
import { formatDateTimeLong, formatHold } from '../../lib/format';
import { useNow } from '../../lib/hooks';

const TONES: TicketTone[] = ['lime', 'pink', 'orange', 'blue', 'yellow'];
const ACCENTS = ['#d8ff3c', '#ff2e93', '#ff5a1f', '#75c8ff', '#f0dc5a'];

export function MyTicketsPage() {
  const [searchParams] = useSearchParams();
  const newTicketId = searchParams.get('new');
  const toast = useToast();
  const now = useNow(1000);

  const [tickets, setTickets] = useState<MyTicket[]>([]);
  const [reservations, setReservations] = useState<MyReservation[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [flippedId, setFlippedId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [ticketPage, reservationResponse] = await Promise.all([
        apiRequest<CursorPage<MyTicket>>('/me/tickets?limit=20'),
        apiRequest<MyReservation[]>('/me/reservations'),
      ]);

      let items = ticketPage.items;
      let cursor = ticketPage.nextCursor;
      let more = ticketPage.hasMore;

      if (newTicketId && !items.some((ticket) => ticket.ticketId === newTicketId)) {
        while (more && cursor) {
          try {
            const page = await apiRequest<CursorPage<MyTicket>>(
              `/me/tickets?limit=20&cursor=${encodeURIComponent(cursor)}`,
            );
            items = [...items, ...page.items];
            cursor = page.nextCursor;
            more = page.hasMore;
          } catch (caught) {
            toast.error(errorMessage(caught, 'Could not load the new ticket.'));
            break;
          }

          if (items.some((ticket) => ticket.ticketId === newTicketId)) {
            break;
          }
        }
      }

      setTickets(items);
      setNextCursor(cursor);
      setHasMore(more);
      setReservations(reservationResponse);
    } catch (caught) {
      setError(errorMessage(caught, 'Could not load your tickets.'));
    } finally {
      setLoading(false);
    }
  }, [newTicketId, toast]);

  const loadMore = useCallback(async () => {
    if (!nextCursor || loadingMore) {
      return;
    }

    setLoadingMore(true);
    try {
      const page = await apiRequest<CursorPage<MyTicket>>(
        `/me/tickets?limit=20&cursor=${encodeURIComponent(nextCursor)}`,
      );
      setTickets((current) => [...current, ...page.items]);
      setNextCursor(page.nextCursor);
      setHasMore(page.hasMore);
    } catch (caught) {
      toast.error(errorMessage(caught, 'Could not load more tickets.'));
    } finally {
      setLoadingMore(false);
    }
  }, [loadingMore, nextCursor, toast]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading) {
    return <Loading label="Loading tickets" />;
  }

  if (error) {
    return (
      <div className="wrap sec">
        <ErrorState message={error} onRetry={() => void load()} />
      </div>
    );
  }

  const holds = reservations.filter(
    (reservation) =>
      reservation.status === 'Held' &&
      !reservation.isExpired &&
      new Date(reservation.expiresAt).getTime() > now,
  );

  const copyCode = async (code: string) => {
    try {
      await navigator.clipboard.writeText(code);
      toast.success(`Code ${code} copied.`);
    } catch {
      toast.error('Could not copy the code. Select it manually.');
    }
  };

  return (
    <div className="wrap sec">
      <div className="page-head">
        <div>
          <span className="mono muted" style={{ fontSize: '.7rem' }}>
            Your wallet of nights
          </span>
          <h1 className="h2">My tickets</h1>
        </div>
        <span className="status s-approved">{tickets.length} shown</span>
      </div>

      {holds.length > 0 ? (
        <div className="rows" style={{ marginBottom: 34 }}>
          {holds.map((hold) => (
            <div className="row" key={hold.id}>
              <div className="row-date" style={{ '--c': 'var(--pink)' } as CSSProperties}>
                <b>{formatHold(new Date(hold.expiresAt).getTime() - now)}</b>
                <span>left</span>
              </div>
              <div className="row-main">
                <h3>{hold.eventTitle}</h3>
                <span className="muted">
                  {hold.ticketTypeName} · {formatDateTimeLong(hold.startsAt)}
                </span>
              </div>
              <div />
              <div className="row-act">
                <Link className="btn btn-sm" to={`/checkout/${hold.id}`}>
                  Finish checkout
                </Link>
              </div>
            </div>
          ))}
        </div>
      ) : null}

      {tickets.length === 0 ? (
        <EmptyState
          title="No tickets yet"
          message="Your next good night is one reservation away. Browse what's on and pick a seat."
          actionLabel="Discover events"
          actionTo="/"
        />
      ) : (
        <div className="stage-list">
          {tickets.map((ticket, index) => {
            const tone = TONES[index % TONES.length];
            const flipped = flippedId === ticket.ticketId;
            const isNew = newTicketId === ticket.ticketId;

            return (
              <article
                className={`t-item${isNew ? ' new' : ''}`}
                key={ticket.ticketId}
                style={{ '--c': ACCENTS[index % ACCENTS.length] } as CSSProperties}
              >
                {isNew ? <span className="sticker t-new">New</span> : null}
                <div
                  className={`flip${flipped ? ' is-flipped' : ''}`}
                  role="button"
                  tabIndex={0}
                  aria-label={`Flip ticket for ${ticket.eventTitle}`}
                  aria-pressed={flipped}
                  onClick={() => setFlippedId(flipped ? null : ticket.ticketId)}
                  onKeyDown={(keyboardEvent) => {
                    if (keyboardEvent.key === 'Enter' || keyboardEvent.key === ' ') {
                      keyboardEvent.preventDefault();
                      setFlippedId(flipped ? null : ticket.ticketId);
                    }
                  }}
                >
                  <div className="flip-inner">
                    <div className="face">
                      <Ticket
                        title={ticket.eventTitle}
                        venue={ticket.venue}
                        startsAt={ticket.startsAt}
                        organizerName={ticket.organizerName}
                        ticketTypeName={ticket.ticketTypeName}
                        code={ticket.code}
                        pricePaid={ticket.pricePaid}
                        checkedInAt={ticket.checkedInAt}
                        tone={tone}
                      />
                    </div>
                    <div className="face back">
                      <TicketBack
                        title={ticket.eventTitle}
                        venue={ticket.venue}
                        startsAt={ticket.startsAt}
                        organizerName={ticket.organizerName}
                        ticketTypeName={ticket.ticketTypeName}
                        code={ticket.code}
                        pricePaid={ticket.pricePaid}
                        tone={tone}
                      />
                    </div>
                  </div>
                </div>
                <div className="t-cap">
                  <span>
                    {ticket.checkedInAt
                      ? `Checked in ${formatDateTimeLong(ticket.checkedInAt)}`
                      : 'Tap the ticket to see the gate details.'}
                  </span>
                  <button
                    type="button"
                    className="pill"
                    onClick={() => void copyCode(ticket.code)}
                  >
                    <CopyIcon size={14} /> {ticket.code}
                  </button>
                </div>
              </article>
            );
          })}
        </div>
      )}

      {hasMore ? (
        <div style={{ marginTop: 18, textAlign: 'center' }}>
          <button
            type="button"
            className="btn btn-ghost"
            disabled={loadingMore}
            onClick={() => void loadMore()}
          >
            {loadingMore ? 'Loading...' : 'Load more'}
          </button>
        </div>
      ) : null}
    </div>
  );
}
