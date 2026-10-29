import type { CSSProperties } from 'react';
import { formatDateTimeLong, formatMoney, getDateParts } from '../lib/format';
import { Barcode } from './Icons';

export type TicketTone = 'lime' | 'pink' | 'orange' | 'blue' | 'yellow';

const TONES: Record<TicketTone, { accent: string; art1: string; art2: string }> = {
  lime: { accent: '#d8ff3c', art1: '#23302a', art2: '#ff5a1f' },
  pink: { accent: '#ff2e93', art1: '#2f1730', art2: '#ff9f1c' },
  orange: { accent: '#ff5a1f', art1: '#311d18', art2: '#d8ff3c' },
  blue: { accent: '#75c8ff', art1: '#132535', art2: '#1fb89a' },
  yellow: { accent: '#f0dc5a', art1: '#2b2713', art2: '#ff2e93' },
};

export interface TicketProps {
  title: string;
  venue: string;
  startsAt: string;
  organizerName?: string;
  ticketTypeName?: string;
  code?: string | null;
  pricePaid?: number | null;
  checkedInAt?: string | null;
  tone?: TicketTone;
  codePlaceholder?: string;
  className?: string;
}

function ticketStyle(tone: TicketTone): CSSProperties {
  const colors = TONES[tone];
  return {
    '--c': colors.accent,
    '--art1': colors.art1,
    '--art2': colors.art2,
  } as CSSProperties;
}

export function Ticket({
  title,
  venue,
  startsAt,
  organizerName = 'EventTicketing',
  ticketTypeName = 'General admission',
  code,
  pricePaid,
  checkedInAt,
  tone = 'lime',
  codePlaceholder = 'SAMPLE',
  className,
}: TicketProps) {
  const date = getDateParts(startsAt);
  const shownCode = code || codePlaceholder;
  const used = Boolean(checkedInAt);

  return (
    <div className={`tk-wrap${className ? ` ${className}` : ''}`}>
      <div className="ticket" style={ticketStyle(tone)}>
        <div className="tk-main">
          <div className="tk-art" />
          <div className="tk-top">
            <span>
              E-TICKET <b>VOL. 01</b>
            </span>
            <span>ADMIT ONE</span>
          </div>
          <div>
            <h3 className="tk-title">{title}</h3>
            <div className="tk-year" aria-hidden="true">
              <span>{date.year.slice(0, 2)}</span>
              <span>{date.year.slice(2)}</span>
            </div>
          </div>
          <div className="tk-foot">
            <div className="tk-date">
              <span>{date.weekday}</span>
              <b>{date.day}</b>
              <span>{date.month}</span>
            </div>
            <div className="tk-info">
              <em>{venue}</em>
              <span>{date.time} WAT</span>
              <span>{organizerName}</span>
            </div>
          </div>
          {used ? <span className="sticker pink tk-used">Used</span> : null}
        </div>
        <div className="tk-stub">
          <div className="tk-stub-info">
            <span>Ticket type</span>
            <b>{ticketTypeName}</b>
            <span>{pricePaid != null ? formatMoney(pricePaid) : 'Admit one'}</span>
            <span>
              {date.weekday} {date.day} {date.month}
            </span>
          </div>
          <div className="tk-code">
            <Barcode code={shownCode} />
            <span>{shownCode}</span>
          </div>
          <span className="mono">Scan at gate</span>
        </div>
      </div>
    </div>
  );
}

export function TicketBack({
  title,
  venue,
  startsAt,
  organizerName = 'EventTicketing',
  ticketTypeName = 'General admission',
  code,
  pricePaid,
  tone = 'lime',
}: TicketProps) {
  const date = getDateParts(startsAt);

  return (
    <div className="tk-back" style={ticketStyle(tone)}>
      <div className="tk-back-head">
        <span className="mono">E-ticket · back</span>
        <h3>{title}</h3>
      </div>
      <dl className="tk-back-grid">
        <div>
          <dt>When</dt>
          <dd>{formatDateTimeLong(startsAt)}</dd>
        </div>
        <div>
          <dt>Where</dt>
          <dd>{venue}</dd>
        </div>
        <div>
          <dt>Ticket</dt>
          <dd>{ticketTypeName}</dd>
        </div>
        <div>
          <dt>Paid</dt>
          <dd>{pricePaid != null ? formatMoney(pricePaid) : '—'}</dd>
        </div>
        <div>
          <dt>Organizer</dt>
          <dd>{organizerName}</dd>
        </div>
        <div>
          <dt>Gate</dt>
          <dd>
            {date.weekday} {date.day} {date.month}, {date.time}
          </dd>
        </div>
      </dl>
      <div className="tk-back-code">
        <Barcode code={code || 'SAMPLE'} />
        <strong>{code || 'SAMPLE'}</strong>
      </div>
      <p className="tk-back-fine">
        One person, one scan. This code is single-use and non-transferable. Bring a valid
        ID. No refunds after entry.
      </p>
    </div>
  );
}
