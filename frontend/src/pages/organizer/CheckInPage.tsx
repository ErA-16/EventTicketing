import { useRef, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import {
  apiRequest,
  errorMessage,
  isApiError,
} from '../../api/client';
import type { CheckInResponse } from '../../api/types';
import { CheckIcon, CloseIcon } from '../../components/Icons';
import { useToast } from '../../context/ToastContext';
import { formatTime } from '../../lib/format';

interface RecentCheckIn {
  code: string;
  eventTitle: string;
}

interface CheckInResult {
  ok: boolean;
  message: string;
  ticket?: CheckInResponse;
}

export function CheckInPage() {
  const toast = useToast();
  const inputRef = useRef<HTMLInputElement>(null);
  const [code, setCode] = useState('');
  const [checking, setChecking] = useState(false);
  const [result, setResult] = useState<CheckInResult | null>(null);
  const [recent, setRecent] = useState<RecentCheckIn[]>([]);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalized = code.trim().toUpperCase();
    if (!normalized) {
      setResult({ ok: false, message: 'Enter a ticket code first.' });
      return;
    }

    setChecking(true);
    setResult(null);
    try {
      const ticket = await apiRequest<CheckInResponse>('/tickets/check-in', {
        method: 'POST',
        body: { code: normalized },
      });
      setResult({
        ok: true,
        message: 'Valid ticket. Let them in.',
        ticket,
      });
      setRecent((current) =>
        [
          { code: ticket.code, eventTitle: ticket.eventTitle },
          ...current.filter((item) => item.code !== ticket.code),
        ].slice(0, 6),
      );
      setCode('');
      toast.success(`${ticket.code} checked in.`);
    } catch (caught) {
      const message =
        isApiError(caught) && caught.status === 409
          ? 'This ticket has already been checked in.'
          : isApiError(caught) && caught.status === 404
            ? 'Ticket not found for one of your events.'
            : errorMessage(caught);
      setResult({ ok: false, message });
    } finally {
      setChecking(false);
      window.setTimeout(() => inputRef.current?.focus(), 0);
    }
  };

  return (
    <div className="wrap sec">
      <Link className="backlink" to="/organizer">
        Organizer desk
      </Link>

      <div className="page-head">
        <div>
          <span className="mono muted" style={{ fontSize: '.7rem' }}>
            Door mode
          </span>
          <h1 className="h2">Check in</h1>
          <p className="muted" style={{ marginTop: 8 }}>
            Type the code from the guest&apos;s ticket. Each code works once.
          </p>
        </div>
      </div>

      <div className="co">
        <div className="panel">
          <form onSubmit={submit}>
            <div className="field">
              <label htmlFor="code">Ticket code</label>
              <div className="ci">
                <input
                  id="code"
                  ref={inputRef}
                  type="text"
                  value={code}
                  onChange={(inputEvent) => {
                    setCode(inputEvent.target.value.toUpperCase());
                    setResult(null);
                  }}
                  placeholder="ABC2DEF3"
                  autoComplete="off"
                  autoCapitalize="characters"
                  spellCheck={false}
                  maxLength={20}
                  style={{ textTransform: 'uppercase' }}
                  autoFocus
                />
                <button className="btn" type="submit" disabled={checking}>
                  {checking ? 'Checking…' : 'Check in'}
                </button>
              </div>
            </div>
          </form>

          {result ? (
            <div className={`result ${result.ok ? 'ok' : 'bad'}`} role="status">
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                {result.ok ? <CheckIcon size={24} /> : <CloseIcon size={24} />}
                <b>{result.ok ? 'Valid ticket' : 'Not accepted'}</b>
              </div>
              <span>{result.message}</span>
              {result.ticket ? (
                <>
                  <span className="mono">
                    {result.ticket.code} · {result.ticket.eventTitle}
                  </span>
                  <span>
                    Checked in at {formatTime(result.ticket.checkedInAt)} WAT
                  </span>
                </>
              ) : null}
            </div>
          ) : null}

          <p className="fine">
            The first scan wins. If two devices scan at the same moment, only one
            request succeeds.
          </p>
        </div>

        <aside className="panel">
          <div className="panel-head">
            <h2 className="h3">This session</h2>
            <span className="status s-draft">{recent.length} scanned</span>
          </div>
          {recent.length === 0 ? (
            <div className="note">
              Successful check-ins will appear here so the door team can glance at
              the latest codes.
            </div>
          ) : (
            <div className="stack">
              {recent.map((item) => (
                <div
                  key={item.code}
                  style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    gap: 12,
                    borderBottom: '1px solid var(--line)',
                    paddingBottom: 10,
                  }}
                >
                  <span className="mono">{item.code}</span>
                  <span className="muted" style={{ textAlign: 'right' }}>
                    {item.eventTitle}
                  </span>
                </div>
              ))}
            </div>
          )}
          <p className="fine">
            This list is local to the page and clears on refresh. It is not a
            replacement for an attendance report.
          </p>
        </aside>
      </div>
    </div>
  );
}
