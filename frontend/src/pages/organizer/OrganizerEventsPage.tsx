import { useCallback, useEffect, useState, type CSSProperties } from 'react';
import { Link } from 'react-router-dom';
import { apiRequest, errorMessage } from '../../api/client';
import type { EventResponse } from '../../api/types';
import { EmptyState, ErrorState, Loading } from '../../components/Feedback';
import { ArrowIcon, CalendarIcon, PinIcon } from '../../components/Icons';
import { StatusPill } from '../../components/StatusPill';
import { useToast } from '../../context/ToastContext';
import { formatDateTime, getDateParts } from '../../lib/format';

const ACCENTS = ['#d8ff3c', '#ff2e93', '#ff5a1f', '#75c8ff', '#f0dc5a', '#1fb89a'];

export function OrganizerEventsPage() {
  const toast = useToast();
  const [events, setEvents] = useState<EventResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [publishingId, setPublishingId] = useState<string | null>(null);
  const [query, setQuery] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setEvents(await apiRequest<EventResponse[]>('/organizer/events'));
    } catch (caught) {
      setError(errorMessage(caught, 'Could not load your events.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const publish = async (eventId: string) => {
    setPublishingId(eventId);
    try {
      await apiRequest<EventResponse>(`/organizer/events/${eventId}/publish`, {
        method: 'POST',
      });
      toast.success('Event published. It is live on the public list.');
      await load();
    } catch (caught) {
      toast.error(errorMessage(caught));
    } finally {
      setPublishingId(null);
    }
  };

  if (loading) {
    return <Loading label="Loading your events" />;
  }

  if (error) {
    return (
      <div className="wrap sec">
        <ErrorState message={error} onRetry={() => void load()} />
      </div>
    );
  }

  const term = query.trim().toLowerCase();
  const filteredEvents = term
    ? events.filter(
        (event) =>
          event.title.toLowerCase().includes(term) ||
          event.venue.toLowerCase().includes(term),
      )
    : events;

  return (
    <div className="wrap sec">
      <div className="page-head">
        <div>
          <span className="mono muted" style={{ fontSize: '.7rem' }}>
            Organizer desk
          </span>
          <h1 className="h2">My events</h1>
          <p className="muted" style={{ marginTop: 8 }}>
            Draft, submit for review, publish, and watch stock move.
          </p>
        </div>
        <div className="cta-row" style={{ marginTop: 0 }}>
          <Link className="btn" to="/organizer/events/new">
            Create event <ArrowIcon />
          </Link>
          <Link className="btn btn-ghost" to="/organizer/check-in">
            Check in
          </Link>
        </div>
      </div>

      {events.length > 0 ? (
        <div className="panel" style={{ marginBottom: 22 }}>
          <div className="field">
            <label htmlFor="organizer-search">Search your events</label>
            <input
              id="organizer-search"
              type="search"
              value={query}
              onChange={(inputEvent) => setQuery(inputEvent.target.value)}
              placeholder="Search title or venue"
            />
          </div>
        </div>
      ) : null}

      {events.length === 0 ? (
        <EmptyState
          title="No events yet"
          message="Create your first event, add ticket types, and send it for review."
          actionLabel="Create event"
          actionTo="/organizer/events/new"
        />
      ) : filteredEvents.length === 0 ? (
        <EmptyState
          title="No matching events"
          message={`Nothing matches "${query.trim()}". Try another title or venue.`}
        />
      ) : (
        <div className="rows">
          {filteredEvents.map((event, index) => {
            const date = getDateParts(event.startsAt);
            const sold = event.ticketTypes.reduce((total, tier) => total + tier.sold, 0);
            const held = event.ticketTypes.reduce((total, tier) => total + tier.held, 0);
            const accent = ACCENTS[index % ACCENTS.length];

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
                      alignItems: 'center',
                      gap: 10,
                    }}
                  >
                    <h3>{event.title}</h3>
                    <StatusPill status={event.status} />
                  </div>
                  <div className="meta">
                    <CalendarIcon size={14} /> {formatDateTime(event.startsAt)}
                  </div>
                  <div className="meta">
                    <PinIcon size={14} /> {event.venue}
                  </div>
                </div>
                <div className="row-main">
                  <span className="mono muted" style={{ fontSize: '.66rem' }}>
                    {event.ticketsLeft} remaining
                  </span>
                  <span className="muted" style={{ fontSize: '.82rem' }}>
                    {sold} sold · {held} held
                  </span>
                  <div className="bar" aria-hidden="true">
                    <i
                      className="sold"
                      style={{
                        width: `${
                          sold + held + event.ticketsLeft > 0
                            ? (sold / (sold + held + event.ticketsLeft)) * 100
                            : 0
                        }%`,
                      }}
                    />
                  </div>
                </div>
                <div className="row-act">
                  <Link className="btn btn-ghost btn-sm" to={`/organizer/events/${event.id}`}>
                    Manage
                  </Link>
                  {event.status === 'Draft' ? (
                    <Link
                      className="btn btn-sm"
                      to={`/organizer/events/${event.id}/edit`}
                    >
                      Edit
                    </Link>
                  ) : null}
                  {event.status === 'Approved' ? (
                    <button
                      type="button"
                      className="btn btn-lime btn-sm"
                      disabled={publishingId === event.id}
                      onClick={() => void publish(event.id)}
                    >
                      {publishingId === event.id ? 'Publishing…' : 'Publish'}
                    </button>
                  ) : null}
                  {event.status === 'Published' ? (
                    <Link className="btn btn-sm" to={`/events/${event.id}`}>
                      View live
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
