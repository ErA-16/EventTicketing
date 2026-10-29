import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { apiRequest, errorMessage, isApiError } from '../../api/client';
import type { EventResponse } from '../../api/types';
import { ErrorState, Loading } from '../../components/Feedback';
import { ArrowIcon, CalendarIcon, PinIcon } from '../../components/Icons';
import { StatusPill } from '../../components/StatusPill';
import { useToast } from '../../context/ToastContext';
import {
  formatDateTime,
  formatDateTimeLong,
  formatMoney,
  formatNumber,
} from '../../lib/format';

export function OrganizerManagePage() {
  const { id } = useParams<{ id: string }>();
  const toast = useToast();
  const [event, setEvent] = useState<EventResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [publishing, setPublishing] = useState(false);

  const load = useCallback(async () => {
    if (!id) {
      setError('Event not found.');
      setLoading(false);
      return;
    }

    setLoading(true);
    setError(null);
    try {
      setEvent(await apiRequest<EventResponse>(`/organizer/events/${id}`));
    } catch (caught) {
      setError(errorMessage(caught, 'Could not load this event.'));
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    void load();
  }, [load]);

  const submit = async () => {
    if (!event) {
      return;
    }

    setSubmitting(true);
    setActionError([]);
    try {
      const updated = await apiRequest<EventResponse>(
        `/organizer/events/${event.id}/submit`,
        { method: 'POST' },
      );
      setEvent(updated);
      toast.success('Sent for admin review.');
    } catch (caught) {
      if (isApiError(caught) && caught.details.length > 0) {
        setActionError(caught.details);
      } else {
        setActionError([errorMessage(caught)]);
      }
    } finally {
      setSubmitting(false);
    }
  };

  const publish = async () => {
    if (!event) {
      return;
    }

    setPublishing(true);
    setActionError([]);
    try {
      const updated = await apiRequest<EventResponse>(
        `/organizer/events/${event.id}/publish`,
        { method: 'POST' },
      );
      setEvent(updated);
      toast.success('Event published.');
    } catch (caught) {
      setActionError([errorMessage(caught)]);
    } finally {
      setPublishing(false);
    }
  };

  if (loading) {
    return <Loading label="Loading event" />;
  }

  if (error || !event) {
    return (
      <div className="wrap sec">
        <ErrorState message={error ?? 'Event not found.'} onRetry={() => void load()} />
      </div>
    );
  }

  const sold = event.ticketTypes.reduce((total, tier) => total + tier.sold, 0);
  const held = event.ticketTypes.reduce((total, tier) => total + tier.held, 0);

  return (
    <div className="wrap sec">
      <Link className="backlink" to="/organizer">
        <ArrowIcon size={16} /> My events
      </Link>

      <div className="page-head">
        <div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'center' }}>
            <h1 className="h2" style={{ fontSize: 'clamp(1.8rem, 5vw, 3rem)' }}>
              {event.title}
            </h1>
            <StatusPill status={event.status} />
          </div>
          <p className="muted" style={{ marginTop: 8 }}>
            {event.ticketsLeft} tickets left · {sold} sold · {held} held
          </p>
        </div>
        <div className="cta-row" style={{ marginTop: 0 }}>
          {event.status === 'Draft' ? (
            <>
              <Link
                className="btn btn-ghost"
                to={`/organizer/events/${event.id}/edit`}
              >
                Edit draft
              </Link>
              <button
                type="button"
                className="btn"
                disabled={submitting}
                onClick={() => void submit()}
              >
                {submitting ? 'Submitting…' : 'Submit for review'} <ArrowIcon />
              </button>
            </>
          ) : null}
          {event.status === 'Approved' ? (
            <button
              type="button"
              className="btn btn-lime"
              disabled={publishing}
              onClick={() => void publish()}
            >
              {publishing ? 'Publishing…' : 'Publish event'} <ArrowIcon />
            </button>
          ) : null}
          {event.status === 'Published' ? (
            <Link className="btn" to={`/events/${event.id}`}>
              View public page <ArrowIcon />
            </Link>
          ) : null}
        </div>
      </div>

      {actionError.length > 0 ? (
        <div className="alert">
          Fix these before continuing:
          <ul>
            {actionError.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </div>
      ) : null}

      {event.status === 'PendingApproval' ? (
        <div className="banner">
          <span>This event is waiting for admin review. You can&apos;t edit it now.</span>
        </div>
      ) : null}

      {event.status === 'Approved' ? (
        <div className="banner">
          <span>Approved! Publish it to make it visible on the public event list.</span>
          <button
            type="button"
            className="btn btn-sm"
            disabled={publishing}
            onClick={() => void publish()}
          >
            Publish now
          </button>
        </div>
      ) : null}

      {event.status === 'Suspended' ? (
        <div className="banner">
          <span>
            This event is suspended by an admin. It is hidden from the public. Once
            unsuspended it returns to Approved and needs publishing again.
          </span>
        </div>
      ) : null}

      {event.rejectionReason ? (
        <div className="alert">
          Rejected: {event.rejectionReason}
          <div className="hint" style={{ marginTop: 6 }}>
            Edit the draft and submit it again.
          </div>
        </div>
      ) : null}

      <div className="co">
        <div className="panel">
          <div className="panel-head">
            <h2 className="h3">Event details</h2>
            <span className="status s-draft">{event.organizerName}</span>
          </div>
          <p style={{ whiteSpace: 'pre-wrap', marginBottom: 18 }}>
            {event.description}
          </p>
          <div className="ev-meta">
            <div>
              <dt className="mono">Starts</dt>
              <dd>
                <CalendarIcon size={14} /> {formatDateTimeLong(event.startsAt)}
              </dd>
            </div>
            <div>
              <dt className="mono">Venue</dt>
              <dd>
                <PinIcon size={14} /> {event.venue}
              </dd>
            </div>
            <div>
              <dt className="mono">Created by</dt>
              <dd>{event.organizerName}</dd>
            </div>
          </div>
        </div>

        <div className="panel">
          <h2 className="h3">Cover image</h2>
          <div
            className="ev-photo ph"
            style={{
              marginTop: 14,
              backgroundImage: event.imageUrl
                ? `url("${event.imageUrl}")`
                : undefined,
            }}
          >
            {!event.imageUrl ? <span className="ph-label">NO IMAGE</span> : null}
          </div>
          {event.status === 'Draft' ? (
            <p className="fine">Upload or replace the cover image in the draft editor.</p>
          ) : null}
        </div>
      </div>

      <div className="panel" style={{ marginTop: 20 }}>
        <div className="panel-head">
          <h2 className="h3">Ticket types</h2>
          <span className="status s-draft">{event.ticketTypes.length} types</span>
        </div>

        {event.ticketTypes.length === 0 ? (
          <div className="note">
            No ticket types yet. Add at least one before sending for review.
          </div>
        ) : (
          <div className="tbl-wrap">
            <table className="tbl">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Price</th>
                  <th>Sales open</th>
                  <th>Early bird</th>
                  <th>Stock</th>
                  <th>Remaining</th>
                </tr>
              </thead>
              <tbody>
                {event.ticketTypes.map((tier) => (
                  <tr key={tier.id}>
                    <td>
                      <strong>{tier.name}</strong>
                    </td>
                    <td className="num">
                      {formatMoney(tier.price)}
                      {tier.earlyBirdPrice != null ? (
                        <div className="hint">
                          EB {formatMoney(tier.earlyBirdPrice)}
                          {tier.earlyBirdEndsAt
                            ? ` until ${formatDateTime(tier.earlyBirdEndsAt)}`
                            : ''}
                        </div>
                      ) : null}
                    </td>
                    <td className="num">{formatDateTime(tier.salesOpenAt)}</td>
                    <td className="num">
                      {tier.earlyBirdEndsAt ? formatDateTime(tier.earlyBirdEndsAt) : '—'}
                    </td>
                    <td className="num">
                      {formatNumber(tier.sold)} sold · {formatNumber(tier.held)} held
                    </td>
                    <td className="num">
                      <strong>{formatNumber(tier.remaining)}</strong> /{' '}
                      {formatNumber(tier.quantity)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
