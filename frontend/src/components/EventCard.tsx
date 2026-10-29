import type { CSSProperties } from 'react';
import { useNavigate } from 'react-router-dom';
import type { EventSummary } from '../api/types';
import { formatMoney, getDateParts } from '../lib/format';
import { useFavorite } from '../lib/favorites';
import { ArrowIcon, HeartIcon, PinIcon } from './Icons';

const ACCENTS = ['#d8ff3c', '#ff2e93', '#ff5a1f', '#75c8ff', '#f0dc5a', '#1fb89a'];

export function EventCard({ event, index = 0 }: { event: EventSummary; index?: number }) {
  const navigate = useNavigate();
  const [saved, toggleSaved] = useFavorite(event.id);
  const date = getDateParts(event.startsAt);
  const accent = ACCENTS[index % ACCENTS.length];

  const open = () => navigate(`/events/${event.id}`);

  return (
    <article
      className="card"
      style={{ '--c': accent } as CSSProperties}
      role="link"
      tabIndex={0}
      onClick={open}
      onKeyDown={(keyboardEvent) => {
        if (keyboardEvent.key === 'Enter' || keyboardEvent.key === ' ') {
          keyboardEvent.preventDefault();
          open();
        }
      }}
      aria-label={`${event.title} at ${event.venue}`}
    >
      <div
        className={`card-img ph${event.imageUrl ? ' is-photo' : ''}`}
        style={event.imageUrl ? { backgroundImage: `url("${event.imageUrl}")` } : undefined}
      >
        <div className="date-badge">
          <b>{date.day}</b>
          <span>{date.month}</span>
        </div>
        <button
          type="button"
          className="heart"
          aria-label={saved ? 'Remove from saved events' : 'Save event'}
          aria-pressed={saved}
          onClick={(clickEvent) => {
            clickEvent.stopPropagation();
            toggleSaved();
          }}
        >
          <HeartIcon />
        </button>
        {!event.imageUrl ? <span className="ph-label">PHOTO PENDING</span> : null}
      </div>
      <div className="card-body">
        <h3>{event.title}</h3>
        <div className="meta">
          <PinIcon />
          <span>{event.venue}</span>
        </div>
        <div className="meta">
          <span className="mono" style={{ fontSize: '.68rem' }}>
            {date.weekday} · {date.time} WAT
          </span>
        </div>
        <div className="card-foot">
          <span className="mono">
            {event.ticketsLeft > 0 ? `${event.ticketsLeft} tickets left` : 'Sold out'}
          </span>
          <span>
            <b>{event.minPrice > 0 ? `From ${formatMoney(event.minPrice)}` : 'Free'}</b>
            <ArrowIcon size={16} className="card-arrow" />
          </span>
        </div>
      </div>
    </article>
  );
}
