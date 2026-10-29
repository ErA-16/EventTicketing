import { useCallback, useEffect, useState, type CSSProperties, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { apiRequest, errorMessage } from '../../api/client';
import type { CursorPage, EventResponse, EventStatus } from '../../api/types';
import { EmptyState, ErrorState, Loading } from '../../components/Feedback';
import { CalendarIcon, PinIcon, SearchIcon } from '../../components/Icons';
import { StatusPill } from '../../components/StatusPill';
import { useToast } from '../../context/ToastContext';
import { formatDateTime, getDateParts } from '../../lib/format';

const ACCENTS = ['#ff2e93', '#d8ff3c', '#ff5a1f', '#75c8ff', '#f0dc5a'];
type Filter = 'All' | 'PendingApproval' | 'Published' | 'Suspended';

export function AdminEventsPage() {
  const toast = useToast();
  const [events, setEvents] = useState<EventResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>('All');
  const [busy, setBusy] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [appliedQuery, setAppliedQuery] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams({ status: 'All', limit: '50' });
      if (appliedQuery.trim()) {
        params.set('q', appliedQuery.trim());
      }

      const page = await apiRequest<CursorPage<EventResponse>>(
        `/admin/events?${params.toString()}`,
      );
      setEvents(page.items);
    } catch (caught) {
      setError(errorMessage(caught, 'Could not load events.'));
    } finally {
      setLoading(false);
    }
  }, [appliedQuery]);

  useEffect(() => {
    void load();
  }, [load]);

  const submitSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setAppliedQuery(query);
  };

  const changeStatus = async (
    event: EventResponse,
    action: 'suspend' | 'unsuspend',
  ) => {
    setBusy(event.id);
    try {
      const updated = await apiRequest<EventResponse>(
        `/admin/events/${event.id}/${action}`,
        { method: 'POST' },
      );
      setEvents((current) =>
        current.map((item) => (item.id === updated.id ? updated : item)),
      );
      toast.success(
        action === 'suspend'
          ? 'Event suspended and hidden from the public list.'
          : 'Event unsuspended. It is now Approved and needs publishing.',
      );
    } catch (caught) {
      toast.error(errorMessage(caught));
    } finally {
      setBusy(null);
    }
  };

  if (loading) {
    return <Loading label="Loading events" />;
  }

  if (error) {
    return (
      <div className="wrap sec">
        <ErrorState message={error} onRetry={() => void load()} />
      </div>
    );
  }

  const visible =
    filter === 'All' ? events : events.filter((event) => event.status === filter);

  return (
    <div className="wrap sec">
      <div className="page-head">
        <div>
          <span className="mono muted" style={{ fontSize: '.7rem' }}>
            Admin desk
          </span>
          <h1 className="h2">All events</h1>
          <p className="muted" style={{ marginTop: 8 }}>
            Suspend one event without touching the organizer. Unsuspend returns it
            to Approved, and the organizer publishes again.
          </p>
        </div>
        <Link className="btn btn-ghost" to="/admin">
          Review queue
        </Link>
      </div>

      <div className="panel" style={{ marginBottom: 22 }}>
        <form onSubmit={submitSearch}>
          <div className="ci">
            <div className="field">
              <label htmlFor="event-search">Search title, venue or organizer</label>
              <input
                id="event-search"
                type="search"
                value={query}
                onChange={(inputEvent) => setQuery(inputEvent.target.value)}
                placeholder="e.g. Neon, Lagos or Demo Nights"
              />
            </div>
            <button type="submit" className="btn">
              <SearchIcon /> Search
            </button>
          </div>
        </form>
      </div>

      <div className="filters" style={{ marginBottom: 22 }}>
        {(['All', 'PendingApproval', 'Published', 'Suspended'] as Filter[]).map(
          (value) => (
            <button
              key={value}
              type="button"
              className="pill"
              aria-pressed={filter === value}
              onClick={() => setFilter(value)}
            >
              {value === 'All'
                ? 'All statuses'
                : value === 'PendingApproval'
                  ? 'In review'
                  : value === 'Published'
                    ? 'Published'
                    : 'Suspended'}
            </button>
          ),
        )}
      </div>

      {visible.length === 0 ? (
        <EmptyState
          title="Nothing here"
          message="No events match this filter."
        />
      ) : (
        <div className="rows">
          {visible.map((event, index) => {
            const date = getDateParts(event.startsAt);
            const accent = ACCENTS[index % ACCENTS.length];
            const canSuspend =
              event.status === 'Approved' || event.status === 'Published';
            const canUnsuspend = event.status === 'Suspended';
            const status: EventStatus = event.status;

            return (
              <article
                className="row"
                key={event.id}
                style={{ '--c': accent } as CSSProperties}
              >
                <div className="row-date">
                  <b>{date.day}</b>
                  <span>
                    {date.month}
                    <br />
                    {date.time}
                  </span>
                </div>
                <div className="row-main">
                  <div
                    style={{
                      display: 'flex',
                      flexWrap: 'wrap',
                      gap: 10,
                      alignItems: 'center',
                    }}
                  >
                    <h3>{event.title}</h3>
                    <StatusPill status={status} />
                  </div>
                  <span className="muted">{event.organizerName || 'Unknown organizer'}</span>
                  <div className="meta">
                    <CalendarIcon size={14} /> {formatDateTime(event.startsAt)}
                  </div>
                  <div className="meta">
                    <PinIcon size={14} /> {event.venue}
                  </div>
                  {event.rejectionReason ? (
                    <span className="muted" style={{ fontSize: '.82rem' }}>
                      Last rejection: {event.rejectionReason}
                    </span>
                  ) : null}
                </div>
                <div className="row-main">
                  <span className="mono muted" style={{ fontSize: '.66rem' }}>
                    {event.ticketsLeft} remaining
                  </span>
                  <span className="muted" style={{ fontSize: '.82rem' }}>
                    {event.ticketTypes.length} ticket type
                    {event.ticketTypes.length === 1 ? '' : 's'}
                  </span>
                </div>
                <div className="row-act">
                  {canSuspend ? (
                    <button
                      type="button"
                      className="btn btn-ghost btn-sm"
                      disabled={busy === event.id}
                      onClick={() => void changeStatus(event, 'suspend')}
                    >
                      {busy === event.id ? 'Working…' : 'Suspend'}
                    </button>
                  ) : null}
                  {canUnsuspend ? (
                    <button
                      type="button"
                      className="btn btn-lime btn-sm"
                      disabled={busy === event.id}
                      onClick={() => void changeStatus(event, 'unsuspend')}
                    >
                      {busy === event.id ? 'Working…' : 'Unsuspend'}
                    </button>
                  ) : null}
                  {event.status === 'PendingApproval' ? (
                    <Link className="btn btn-sm" to="/admin">
                      Review
                    </Link>
                  ) : null}
                  {event.status === 'Published' ? (
                    <Link className="btn btn-ghost btn-sm" to={`/events/${event.id}`}>
                      Public page
                    </Link>
                  ) : null}
                </div>
              </article>
            );
          })}
        </div>
      )}
    </div>
  );
}
