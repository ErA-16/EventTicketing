import type { EventStatus, ReservationStatus, Role } from '../api/types';

const TZ = 'Africa/Lagos';

export function formatMoney(value: number): string {
  const fractionDigits = Number.isInteger(value) ? 0 : 2;
  const formatted = new Intl.NumberFormat('en-NG', {
    style: 'currency',
    currency: 'NGN',
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits: fractionDigits,
  }).format(Math.abs(value));

  return value < 0 ? `-${formatted}` : formatted;
}

export function formatNumber(value: number): string {
  return new Intl.NumberFormat('en-NG').format(value);
}

function getParts(date: Date, options: Intl.DateTimeFormatOptions): Record<string, string> {
  const formatter = new Intl.DateTimeFormat('en-GB', { timeZone: TZ, ...options });
  return Object.fromEntries(
    formatter
      .formatToParts(date)
      .filter((part) => part.type !== 'literal')
      .map((part) => [part.type, part.value]),
  );
}

export function formatDateLong(iso: string): string {
  return new Intl.DateTimeFormat('en-NG', {
    timeZone: TZ,
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  }).format(new Date(iso));
}

export function formatDateShort(iso: string): string {
  return new Intl.DateTimeFormat('en-NG', {
    timeZone: TZ,
    weekday: 'short',
    day: 'numeric',
    month: 'short',
  }).format(new Date(iso));
}

export function formatTime(iso: string): string {
  return new Intl.DateTimeFormat('en-NG', {
    timeZone: TZ,
    hour: 'numeric',
    minute: '2-digit',
    hour12: true,
  }).format(new Date(iso));
}

export function formatDateTime(iso: string): string {
  return `${formatDateShort(iso)} · ${formatTime(iso)}`;
}

export function formatDateTimeLong(iso: string): string {
  return `${formatDateLong(iso)} at ${formatTime(iso)} WAT`;
}

export interface DateParts {
  day: string;
  month: string;
  weekday: string;
  time: string;
  year: string;
  monthLong: string;
}

export function getDateParts(iso: string): DateParts {
  const date = new Date(iso);
  const dateOnly = getParts(date, { day: '2-digit', month: 'short', weekday: 'short' });
  const full = getParts(date, { year: 'numeric', month: 'long' });

  return {
    day: dateOnly.day ?? '--',
    month: (dateOnly.month ?? '---').toUpperCase(),
    weekday: (dateOnly.weekday ?? '---').toUpperCase(),
    time: formatTime(iso),
    year: full.year ?? '----',
    monthLong: full.month ?? '',
  };
}

export function formatCountdown(milliseconds: number, includeHours = true): string {
  const total = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;

  if (includeHours) {
    return [hours, minutes, seconds].map((part) => String(part).padStart(2, '0')).join(':');
  }

  return [minutes, seconds].map((part) => String(part).padStart(2, '0')).join(':');
}

export function formatHold(milliseconds: number): string {
  const total = Math.max(0, Math.floor(milliseconds / 1000));
  const minutes = Math.floor(total / 60);
  const seconds = total % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

export function toLocalInputValue(iso: string): string {
  const parts = getParts(new Date(iso), {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  });

  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`;
}

export function fromLocalInputValue(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value);
  if (!match) {
    return value;
  }

  const [, year, month, day, hour, minute] = match;
  // Lagos is UTC+1 all year; the UI always edits times in WAT.
  return new Date(
    Date.UTC(
      Number(year),
      Number(month) - 1,
      Number(day),
      Number(hour) - 1,
      Number(minute),
    ),
  ).toISOString();
}

export function defaultStartsAtInput(daysAhead = 7): string {
  return toLocalInputValue(new Date(Date.now() + daysAhead * 24 * 60 * 60 * 1000).toISOString());
}

export function defaultSalesOpenInput(): string {
  return toLocalInputValue(new Date().toISOString());
}

export function eventStatusLabel(status: EventStatus): string {
  switch (status) {
    case 'PendingApproval':
      return 'In review';
    case 'Approved':
      return 'Approved';
    case 'Published':
      return 'Published';
    case 'Suspended':
      return 'Suspended';
    case 'Draft':
      return 'Draft';
    default:
      return status;
  }
}

export function eventStatusClass(status: EventStatus): string {
  switch (status) {
    case 'PendingApproval':
      return 's-pending';
    case 'Approved':
    case 'Published':
      return 's-approved';
    case 'Suspended':
      return 's-suspended';
    case 'Draft':
      return 's-draft';
    default:
      return 's-draft';
  }
}

export function reservationStatusLabel(status: ReservationStatus): string {
  switch (status) {
    case 'Held':
      return 'Held';
    case 'Paid':
      return 'Paid';
    case 'Expired':
      return 'Expired';
    case 'Cancelled':
      return 'Cancelled';
    default:
      return status;
  }
}

export function roleHome(role: Role | null): string {
  switch (role) {
    case 'Organizer':
      return '/organizer';
    case 'Admin':
      return '/admin';
    default:
      return '/';
  }
}

export function isPast(iso: string): boolean {
  return new Date(iso).getTime() <= Date.now();
}

export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('');
}
