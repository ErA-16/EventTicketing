import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { apiRequest, errorMessage } from '../../api/client';
import type { AdminOrganizer, CursorPage } from '../../api/types';
import { EmptyState, ErrorState, Loading } from '../../components/Feedback';
import { SearchIcon } from '../../components/Icons';
import { useToast } from '../../context/ToastContext';

type SuspendFilter = 'all' | 'active' | 'suspended';

export function AdminOrganizersPage() {
  const toast = useToast();
  const [organizers, setOrganizers] = useState<AdminOrganizer[]>([]);
  const [query, setQuery] = useState('');
  const [appliedQuery, setAppliedQuery] = useState('');
  const [filter, setFilter] = useState<SuspendFilter>('all');
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams({ limit: '50' });
      if (appliedQuery.trim()) {
        params.set('q', appliedQuery.trim());
      }
      if (filter !== 'all') {
        params.set('suspended', String(filter === 'suspended'));
      }

      const page = await apiRequest<CursorPage<AdminOrganizer>>(
        `/admin/organizers?${params.toString()}`,
      );
      setOrganizers(page.items);
      setNextCursor(page.nextCursor);
      setHasMore(page.hasMore);
    } catch (caught) {
      setError(errorMessage(caught, 'Could not load organizers.'));
    } finally {
      setLoading(false);
    }
  }, [appliedQuery, filter]);

  useEffect(() => {
    void load();
  }, [load]);

  const loadMore = async () => {
    if (!nextCursor || loadingMore) {
      return;
    }

    setLoadingMore(true);
    try {
      const params = new URLSearchParams({ limit: '50', cursor: nextCursor });
      if (appliedQuery.trim()) {
        params.set('q', appliedQuery.trim());
      }
      if (filter !== 'all') {
        params.set('suspended', String(filter === 'suspended'));
      }

      const page = await apiRequest<CursorPage<AdminOrganizer>>(
        `/admin/organizers?${params.toString()}`,
      );
      setOrganizers((current) => [...current, ...page.items]);
      setNextCursor(page.nextCursor);
      setHasMore(page.hasMore);
    } catch (caught) {
      toast.error(errorMessage(caught));
    } finally {
      setLoadingMore(false);
    }
  };

  const changeSuspension = async (organizer: AdminOrganizer) => {
    const action = organizer.isSuspended ? 'unsuspend' : 'suspend';
    setBusy(organizer.id);
    try {
      await apiRequest<void>(`/admin/organizers/${organizer.id}/${action}`, {
        method: 'POST',
      });
      setOrganizers((current) =>
        current.map((item) =>
          item.id === organizer.id
            ? { ...item, isSuspended: action === 'suspend' }
            : item,
        ),
      );
      toast.success(
        action === 'suspend'
          ? 'Organizer suspended. Their events are now hidden.'
          : 'Organizer unsuspended.',
      );
    } catch (caught) {
      toast.error(errorMessage(caught));
    } finally {
      setBusy(null);
    }
  };

  const submitSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setAppliedQuery(query);
  };

  return (
    <div className="wrap sec">
      <div className="page-head">
        <div>
          <span className="mono muted" style={{ fontSize: '.7rem' }}>
            Admin desk
          </span>
          <h1 className="h2">Organizers</h1>
          <p className="muted" style={{ marginTop: 8 }}>
            A suspension hides every published event from that organizer without
            changing the event rows.
          </p>
        </div>
        <span className="status s-draft">{organizers.length} shown</span>
      </div>

      <div className="panel">
        <form onSubmit={submitSearch} style={{ marginBottom: 18 }}>
          <div className="ci">
            <div className="field">
              <label htmlFor="search">Search organization, name or email</label>
              <input
                id="search"
                type="search"
                value={query}
                onChange={(inputEvent) => setQuery(inputEvent.target.value)}
                placeholder="e.g. Demo Nights or ada@example.com"
              />
            </div>
            <button type="submit" className="btn">
              <SearchIcon /> Search
            </button>
          </div>
        </form>

        <div className="filters" style={{ marginBottom: 18 }}>
          {(['all', 'active', 'suspended'] as SuspendFilter[]).map((value) => (
            <button
              key={value}
              type="button"
              className="pill"
              aria-pressed={filter === value}
              onClick={() => setFilter(value)}
            >
              {value === 'all'
                ? 'All organizers'
                : value === 'active'
                  ? 'Active'
                  : 'Suspended'}
            </button>
          ))}
        </div>

        {loading ? (
          <Loading label="Loading organizers" />
        ) : error ? (
          <ErrorState message={error} onRetry={() => void load()} />
        ) : organizers.length === 0 ? (
          <EmptyState
            title="No organizers found"
            message="Try a different search or filter. Attendee accounts never appear here."
          />
        ) : (
          <>
            <div className="tbl-wrap">
              <table className="tbl">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Email</th>
                    <th>Status</th>
                    <th>Action</th>
                  </tr>
                </thead>
                <tbody>
                  {organizers.map((organizer) => (
                    <tr key={organizer.id}>
                      <td>
                        <strong>
                          {organizer.organizationName?.trim() ||
                            `${organizer.firstName} ${organizer.lastName}`.trim() ||
                            organizer.email}
                        </strong>
                        {organizer.organizationName &&
                        `${organizer.firstName} ${organizer.lastName}`.trim() ? (
                          <div className="muted" style={{ fontSize: '.8rem' }}>
                            {`${organizer.firstName} ${organizer.lastName}`.trim()}
                          </div>
                        ) : null}
                      </td>
                      <td>{organizer.email}</td>
                      <td>
                        <span
                          className={`status ${
                            organizer.isSuspended ? 's-suspended' : 's-approved'
                          }`}
                        >
                          {organizer.isSuspended ? 'Suspended' : 'Active'}
                        </span>
                      </td>
                      <td>
                        <div className="tbl-actions">
                          <button
                            type="button"
                            className={`btn btn-sm ${
                              organizer.isSuspended ? 'btn-lime' : 'btn-ghost'
                            }`}
                            disabled={busy === organizer.id}
                            onClick={() => void changeSuspension(organizer)}
                          >
                            {busy === organizer.id
                              ? 'Working…'
                              : organizer.isSuspended
                                ? 'Unsuspend'
                                : 'Suspend'}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {hasMore ? (
              <div style={{ marginTop: 18, textAlign: 'center' }}>
                <button
                  type="button"
                  className="btn btn-ghost"
                  disabled={loadingMore}
                  onClick={() => void loadMore()}
                >
                  {loadingMore ? 'Loading…' : 'Load more'}
                </button>
              </div>
            ) : null}
          </>
        )}
      </div>
    </div>
  );
}
