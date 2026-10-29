import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { apiRequest, errorMessage } from '../../api/client';
import type { CursorPage, EventSummary } from '../../api/types';
import { EventCard } from '../../components/EventCard';
import { EmptyState, ErrorState, Loading } from '../../components/Feedback';
import { ArrowIcon, SearchIcon, StarIcon } from '../../components/Icons';
import { Ticket } from '../../components/Ticket';
import { useAuth } from '../../context/AuthContext';
import { useToast } from '../../context/ToastContext';
import { formatMoney } from '../../lib/format';
import { useCountdown } from '../../lib/hooks';

type Filter = 'all' | 'week' | 'free';

export function HomePage() {
  const { role, isAuthenticated } = useAuth();
  const toast = useToast();
  const [allEvents, setAllEvents] = useState<EventSummary[]>([]);
  const [searchResults, setSearchResults] = useState<EventSummary[] | null>(null);
  const [query, setQuery] = useState('');
  const [appliedQuery, setAppliedQuery] = useState('');
  const [searching, setSearching] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>('all');

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const page = await apiRequest<CursorPage<EventSummary>>('/events?limit=50', {
        auth: false,
      });
      setAllEvents(page.items);
    } catch (caught) {
      setError(errorMessage(caught, 'Could not load events.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const hero = useMemo(() => {
    if (allEvents.length === 0) {
      return undefined;
    }

    return allEvents.reduce((earliest, event) =>
      new Date(event.startsAt).getTime() < new Date(earliest.startsAt).getTime()
        ? event
        : earliest,
    );
  }, [allEvents]);
  const countdown = useCountdown(hero?.startsAt);

  const displayEvents = useMemo(
    () => (appliedQuery ? (searchResults ?? []) : allEvents),
    [allEvents, appliedQuery, searchResults],
  );

  const filtered = useMemo(() => {
    if (filter === 'free') {
      return displayEvents.filter((event) => event.minPrice <= 0);
    }

    if (filter === 'week') {
      const end = Date.now() + 7 * 24 * 60 * 60 * 1000;
      return displayEvents.filter((event) => new Date(event.startsAt).getTime() <= end);
    }

    return displayEvents;
  }, [displayEvents, filter]);

  const submitSearch = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const term = query.trim();
    setFilter('all');

    if (!term) {
      setAppliedQuery('');
      setSearchResults(null);
      return;
    }

    setSearching(true);
    try {
      const params = new URLSearchParams({ limit: '50', q: term });
      const page = await apiRequest<CursorPage<EventSummary>>(`/events?${params.toString()}`, {
        auth: false,
      });
      setAppliedQuery(term);
      setSearchResults(page.items);
    } catch (caught) {
      toast.error(errorMessage(caught, 'Could not search events.'));
    } finally {
      setSearching(false);
    }
  };

  const clearSearch = () => {
    setQuery('');
    setAppliedQuery('');
    setSearchResults(null);
    setFilter('all');
  };

  if (loading) {
    return <Loading label="Finding events" />;
  }

  if (error) {
    return (
      <div className="wrap sec">
        <ErrorState message={error} onRetry={() => void load()} />
      </div>
    );
  }

  return (
    <>
      <section className="hero">
        <div className="wrap">
          <span className="sticker">Tickets on sale</span>
          <h1>
            <span>Good nights</span>
            <span>
              start <em>here.</em>
            </span>
          </h1>

          <div className="hero-grid">
            <div>
              <p className="lead">
                Afrobeats on the roof. Comedy after dark. Art in full colour.
                Find your next night out and keep every ticket in one place.
              </p>
              <div className="cta-row">
                <button
                  type="button"
                  className="btn"
                  onClick={() =>
                    document.getElementById('events')?.scrollIntoView({ behavior: 'smooth' })
                  }
                >
                  Browse events <ArrowIcon />
                </button>
                {role === 'Attendee' ? (
                  <Link className="btn btn-ghost" to="/my-tickets">
                    My tickets
                  </Link>
                ) : !isAuthenticated ? (
                  <Link className="btn btn-ghost" to="/signup">
                    Create account
                  </Link>
                ) : null}
              </div>

              {hero ? (
                <div className="cd-block">
                  <div className="mono cd-label">Next event starts in</div>
                  <div className="cd" aria-label="Countdown to next event">
                    <div>
                      <b>{String(countdown.days).padStart(2, '0')}</b>
                      <span>days</span>
                    </div>
                    <div>
                      <b>{String(countdown.hours).padStart(2, '0')}</b>
                      <span>hrs</span>
                    </div>
                    <div>
                      <b>{String(countdown.minutes).padStart(2, '0')}</b>
                      <span>min</span>
                    </div>
                    <div>
                      <b>{String(countdown.seconds).padStart(2, '0')}</b>
                      <span>sec</span>
                    </div>
                  </div>
                </div>
              ) : null}
            </div>

            <div className="hero-stage">
              <span className="sticker s1">
                <StarIcon size={10} /> {hero ? `${hero.ticketsLeft} left` : 'New drops'}
              </span>
              <span className="sticker pink s2">
                From {hero ? formatMoney(hero.minPrice) : '—'}
              </span>
              <Ticket
                className="hero-ticket"
                title={hero?.title ?? 'Your next big night'}
                venue={hero?.venue ?? 'Lagos'}
                startsAt={hero?.startsAt ?? new Date().toISOString()}
                organizerName="EventTicketing presents"
                ticketTypeName="Admit one"
                tone="lime"
                codePlaceholder="YOU'RE IN"
              />
              <div className="badge-round">
                One ticket
                <br />
                One night
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className="wrap sec" id="events">
        <div className="panel" style={{ marginBottom: 24 }}>
          <form className="ci" onSubmit={(event) => void submitSearch(event)}>
            <div className="field">
              <label htmlFor="event-search">Search events or venues</label>
              <input
                id="event-search"
                type="search"
                value={query}
                onChange={(inputEvent) => setQuery(inputEvent.target.value)}
                placeholder="Lagos, comedy, rooftop..."
              />
            </div>
            <button
              type="submit"
              className="btn"
              disabled={searching}
            >
              <SearchIcon /> {searching ? 'Searching' : 'Search'}
            </button>
            {appliedQuery ? (
              <button
                type="button"
                className="btn btn-ghost"
                onClick={clearSearch}
              >
                Clear
              </button>
            ) : null}
          </form>
        </div>

        <div className="sec-head">
          <div>
            <div className="mono muted" style={{ fontSize: '.7rem' }}>
              {appliedQuery ? `${displayEvents.length} matches` : 'Newest first / selling fast'}
            </div>
            <h2 className="h2">{appliedQuery ? 'Search results' : 'Upcoming events'}</h2>
          </div>
          <div className="filters" aria-label="Filter events">
            <button
              type="button"
              className="pill"
              aria-pressed={filter === 'all'}
              onClick={() => setFilter('all')}
            >
              All events
            </button>
            <button
              type="button"
              className="pill"
              aria-pressed={filter === 'week'}
              onClick={() => setFilter('week')}
            >
              Next 7 days
            </button>
            <button
              type="button"
              className="pill"
              aria-pressed={filter === 'free'}
              onClick={() => setFilter('free')}
            >
              Free
            </button>
          </div>
        </div>

        {filtered.length > 0 ? (
          <div className="grid">
            {filtered.map((event, index) => (
              <EventCard key={event.id} event={event} index={index} />
            ))}
          </div>
        ) : (
          <EmptyState
            title={appliedQuery ? 'No matching events' : 'No events here yet'}
            message={
              appliedQuery
                ? `Nothing matched "${appliedQuery}". Try another title or venue.`
                : 'Try another filter, or check back soon - new events land every week.'
            }
          />
        )}
      </section>

      <section className="band-lime">
        <div className="wrap sec">
          <h2 className="h2">Three steps to the gate</h2>
          <div className="steps">
            <article className="step">
              <b>01</b>
              <h3>Find your night</h3>
              <p>Browse what&apos;s on and pick a ticket type that fits.</p>
            </article>
            <article className="step">
              <b>02</b>
              <h3>Hold or buy</h3>
              <p>
                A seat is held for ten minutes while you top up and pay. Buy now
                skips the wait.
              </p>
            </article>
            <article className="step">
              <b>03</b>
              <h3>Show your code</h3>
              <p>Every ticket lives in your account. No printing, no stress.</p>
            </article>
          </div>
        </div>
      </section>

      <section className="wrap sec">
        <div className="panel" style={{ display: 'flex', flexWrap: 'wrap', gap: 20, alignItems: 'center', justifyContent: 'space-between' }}>
          <div>
            <span className="mono muted" style={{ fontSize: '.68rem' }}>
              For organizers
            </span>
            <h2 className="h2" style={{ marginTop: 8 }}>
              Put your event on the list.
            </h2>
            <p className="muted" style={{ marginTop: 8 }}>
              Draft it, send it for review, and go live when approved.
            </p>
          </div>
          <Link className="btn" to={role === 'Organizer' ? '/organizer/events/new' : '/signup'}>
            {role === 'Organizer' ? 'Create event' : 'Become an organizer'} <ArrowIcon />
          </Link>
        </div>
      </section>
    </>
  );
}
