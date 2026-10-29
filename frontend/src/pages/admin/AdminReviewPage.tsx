import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { apiRequest, errorMessage } from '../../api/client';
import type { CursorPage, EventResponse } from '../../api/types';
import { EmptyState, ErrorState, Loading } from '../../components/Feedback';
import { CheckIcon, CloseIcon, SearchIcon } from '../../components/Icons';
import { Modal } from '../../components/Modal';
import { useToast } from '../../context/ToastContext';
import { formatDateTimeLong, formatMoney } from '../../lib/format';

export function AdminReviewPage() {
  const toast = useToast();
  const [events, setEvents] = useState<EventResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [approvingId, setApprovingId] = useState<string | null>(null);
  const [rejecting, setRejecting] = useState<EventResponse | null>(null);
  const [reason, setReason] = useState('');
  const [rejectBusy, setRejectBusy] = useState(false);
  const [query, setQuery] = useState('');
  const [appliedQuery, setAppliedQuery] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams({ limit: '50' });
      if (appliedQuery.trim()) {
        params.set('q', appliedQuery.trim());
      }

      const page = await apiRequest<CursorPage<EventResponse>>(
        `/admin/events?${params.toString()}`,
      );
      setEvents(page.items);
    } catch (caught) {
      setError(errorMessage(caught, 'Could not load the review queue.'));
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

  const approve = async (eventId: string) => {
    setApprovingId(eventId);
    try {
      await apiRequest<EventResponse>(`/admin/events/${eventId}/approve`, {
        method: 'POST',
      });
      setEvents((current) => current.filter((event) => event.id !== eventId));
      toast.success('Event approved. The organizer can publish it now.');
    } catch (caught) {
      toast.error(errorMessage(caught));
    } finally {
      setApprovingId(null);
    }
  };

  const reject = async () => {
    if (!rejecting) {
      return;
    }

    const trimmed = reason.trim();
    if (!trimmed) {
      toast.error('A rejection reason is required.');
      return;
    }

    setRejectBusy(true);
    try {
      await apiRequest<EventResponse>(`/admin/events/${rejecting.id}/reject`, {
        method: 'POST',
        body: { reason: trimmed },
      });
      setEvents((current) => current.filter((event) => event.id !== rejecting.id));
      toast.success('Event rejected and sent back to the organizer as a draft.');
      setRejecting(null);
      setReason('');
    } catch (caught) {
      toast.error(errorMessage(caught));
    } finally {
      setRejectBusy(false);
    }
  };

  if (loading) {
    return <Loading label="Loading review queue" />;
  }

  if (error) {
    return (
      <div className="wrap sec">
        <ErrorState message={error} onRetry={() => void load()} />
      </div>
    );
  }

  return (
    <div className="wrap sec">
      <div className="page-head">
        <div>
          <span className="mono muted" style={{ fontSize: '.7rem' }}>
            Admin desk
          </span>
          <h1 className="h2">Review queue</h1>
          <p className="muted" style={{ marginTop: 8 }}>
            Approve good submissions or reject with a clear reason. Approval sends
            the event back to the organizer to publish.
          </p>
        </div>
        <span className="status s-pending">{events.length} waiting</span>
      </div>

      <div className="panel" style={{ marginBottom: 22 }}>
        <form onSubmit={submitSearch}>
          <div className="ci">
            <div className="field">
              <label htmlFor="review-search">Search title, venue or organizer</label>
              <input
                id="review-search"
                type="search"
                value={query}
                onChange={(inputEvent) => setQuery(inputEvent.target.value)}
                placeholder="e.g. Punchline, Abuja or Demo Nights"
              />
            </div>
            <button type="submit" className="btn">
              <SearchIcon /> Search
            </button>
          </div>
        </form>
      </div>

      {events.length === 0 ? (
        <EmptyState
          title={appliedQuery ? 'No matching submissions' : 'Queue is clear'}
          message={
            appliedQuery
              ? `No pending events match "${appliedQuery}".`
              : 'No events are waiting for review. New submissions will appear here.'
          }
        />
      ) : (
        <div className="grid">
          {events.map((event) => (
            <article className="panel" key={event.id}>
              <div
                className="ev-photo ph"
                style={{
                  aspectRatio: '4/3',
                  marginBottom: 16,
                  backgroundImage: event.imageUrl
                    ? `url("${event.imageUrl}")`
                    : undefined,
                }}
              >
                {!event.imageUrl ? <span className="ph-label">NO IMAGE</span> : null}
              </div>
              <div className="panel-head">
                <h2 className="h3">{event.title}</h2>
                <span className="status s-pending">In review</span>
              </div>
              <p className="muted" style={{ marginBottom: 12 }}>
                {event.organizerName} · {formatDateTimeLong(event.startsAt)}
                <br />
                {event.venue}
              </p>
              <p style={{ whiteSpace: 'pre-wrap', marginBottom: 18 }}>
                {event.description}
              </p>

              <div className="tbl-wrap">
                <table className="tbl" style={{ minWidth: 360 }}>
                  <thead>
                    <tr>
                      <th>Ticket</th>
                      <th>Price</th>
                      <th>Left</th>
                    </tr>
                  </thead>
                  <tbody>
                    {event.ticketTypes.map((tier) => (
                      <tr key={tier.id}>
                        <td>{tier.name}</td>
                        <td className="num">{formatMoney(tier.price)}</td>
                        <td className="num">
                          {tier.remaining} / {tier.quantity}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="form-actions">
                <button
                  type="button"
                  className="btn btn-lime"
                  disabled={approvingId === event.id}
                  onClick={() => void approve(event.id)}
                >
                  <CheckIcon size={16} />
                  {approvingId === event.id ? 'Approving…' : 'Approve'}
                </button>
                <button
                  type="button"
                  className="btn btn-pink"
                  onClick={() => {
                    setRejecting(event);
                    setReason('');
                  }}
                >
                  <CloseIcon size={16} /> Reject
                </button>
              </div>
            </article>
          ))}
        </div>
      )}

      {rejecting ? (
        <Modal title={`Reject ${rejecting.title}`} onClose={() => setRejecting(null)}>
          <p className="muted">
            The organizer sees this reason and can edit the draft before resubmitting.
          </p>
          <div className="field" style={{ marginTop: 16 }}>
            <label htmlFor="reason">Reason (required)</label>
            <textarea
              id="reason"
              value={reason}
              maxLength={1000}
              onChange={(inputEvent) => setReason(inputEvent.target.value)}
              placeholder="What needs fixing?"
              autoFocus
            />
            <span className="hint">{reason.length}/1000</span>
          </div>
          <div className="form-actions">
            <button
              type="button"
              className="btn btn-pink"
              disabled={rejectBusy}
              onClick={() => void reject()}
            >
              {rejectBusy ? 'Rejecting…' : 'Reject event'}
            </button>
            <button
              type="button"
              className="btn btn-ghost"
              onClick={() => setRejecting(null)}
            >
              Cancel
            </button>
          </div>
        </Modal>
      ) : null}
    </div>
  );
}
