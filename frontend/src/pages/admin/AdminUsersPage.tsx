import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { apiRequest, errorMessage } from '../../api/client';
import type { AdminUser, CursorPage } from '../../api/types';
import { EmptyState, ErrorState, Loading } from '../../components/Feedback';
import { SearchIcon } from '../../components/Icons';
import { useToast } from '../../context/ToastContext';

type RoleFilter = 'All' | 'Attendee' | 'Organizer' | 'Admin';

export function AdminUsersPage() {
  const toast = useToast();
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [query, setQuery] = useState('');
  const [appliedQuery, setAppliedQuery] = useState('');
  const [roleFilter, setRoleFilter] = useState<RoleFilter>('All');
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams({ limit: '50' });
      if (appliedQuery.trim()) {
        params.set('q', appliedQuery.trim());
      }
      if (roleFilter !== 'All') {
        params.set('role', roleFilter);
      }

      const page = await apiRequest<CursorPage<AdminUser>>(
        `/admin/users?${params.toString()}`,
      );
      setUsers(page.items);
      setNextCursor(page.nextCursor);
      setHasMore(page.hasMore);
    } catch (caught) {
      setError(errorMessage(caught, 'Could not load users.'));
    } finally {
      setLoading(false);
    }
  }, [appliedQuery, roleFilter]);

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
      if (roleFilter !== 'All') {
        params.set('role', roleFilter);
      }

      const page = await apiRequest<CursorPage<AdminUser>>(
        `/admin/users?${params.toString()}`,
      );
      setUsers((current) => [...current, ...page.items]);
      setNextCursor(page.nextCursor);
      setHasMore(page.hasMore);
    } catch (caught) {
      toast.error(errorMessage(caught));
    } finally {
      setLoadingMore(false);
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
          <h1 className="h2">Users</h1>
          <p className="muted" style={{ marginTop: 8 }}>
            A read-only directory of every account. Search by name, organization,
            or email.
          </p>
        </div>
        <span className="status s-draft">{users.length} shown</span>
      </div>

      <div className="panel">
        <form onSubmit={submitSearch} style={{ marginBottom: 18 }}>
          <div className="ci">
            <div className="field">
              <label htmlFor="user-search">Search name, organization or email</label>
              <input
                id="user-search"
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
          {(['All', 'Attendee', 'Organizer', 'Admin'] as RoleFilter[]).map((value) => (
            <button
              key={value}
              type="button"
              className="pill"
              aria-pressed={roleFilter === value}
              onClick={() => setRoleFilter(value)}
            >
              {value === 'All' ? 'All roles' : value}
            </button>
          ))}
        </div>

        {loading ? (
          <Loading label="Loading users" />
        ) : error ? (
          <ErrorState message={error} onRetry={() => void load()} />
        ) : users.length === 0 ? (
          <EmptyState
            title="No users found"
            message="Try a different search or role filter."
          />
        ) : (
          <>
            <div className="tbl-wrap">
              <table className="tbl">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Email</th>
                    <th>Roles</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {users.map((user) => {
                    const fullName = `${user.firstName} ${user.lastName}`.trim();
                    return (
                      <tr key={user.id}>
                        <td>
                          <strong>
                            {fullName ||
                              user.organizationName?.trim() ||
                              user.displayName}
                          </strong>
                          {fullName && user.organizationName ? (
                            <div className="muted" style={{ fontSize: '.8rem' }}>
                              {user.organizationName}
                            </div>
                          ) : null}
                        </td>
                        <td>{user.email}</td>
                        <td>
                          <div className="tbl-actions">
                            {user.roles.length > 0
                              ? user.roles.map((role) => (
                                  <span className="status s-draft" key={role}>
                                    {role}
                                  </span>
                                ))
                              : '-'}
                          </div>
                        </td>
                        <td>
                          <span
                            className={`status ${
                              user.isSuspended ? 's-suspended' : 's-approved'
                            }`}
                          >
                            {user.isSuspended ? 'Suspended' : 'Active'}
                          </span>
                        </td>
                      </tr>
                    );
                  })}
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
                  {loadingMore ? 'Loading...' : 'Load more'}
                </button>
              </div>
            ) : null}
          </>
        )}
      </div>
    </div>
  );
}
