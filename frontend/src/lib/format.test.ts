import { describe, expect, it } from 'vitest';
import {
  eventStatusClass,
  eventStatusLabel,
  formatCountdown,
  formatHold,
  formatMoney,
  formatNumber,
  fromLocalInputValue,
  getDateParts,
  initials,
  isPast,
  reservationStatusLabel,
  roleHome,
  toLocalInputValue,
} from './format';

describe('formatMoney', () => {
  it('formats whole naira amounts without decimals', () => {
    const result = formatMoney(4000);
    expect(result).toContain('4,000');
    expect(result).not.toContain('.00');
  });

  it('keeps two decimals for kobo and prefixes negatives', () => {
    expect(formatMoney(150.5)).toContain('150.50');
    expect(formatMoney(-500)).toMatch(/^-/);
    expect(formatMoney(-500)).toContain('500');
  });
});

describe('number, date and countdown helpers', () => {
  it('formats grouped numbers', () => {
    expect(formatNumber(1234567)).toBe('1,234,567');
  });

  it('converts between the WAT input value and UTC', () => {
    expect(fromLocalInputValue('2026-10-27T20:30')).toBe('2026-10-27T19:30:00.000Z');
    expect(toLocalInputValue('2026-10-27T19:30:00Z')).toBe('2026-10-27T20:30');
  });

  it('keeps malformed local input unchanged', () => {
    expect(fromLocalInputValue('not-a-date')).toBe('not-a-date');
  });

  it('splits a date into Lagos display parts', () => {
    const parts = getDateParts('2026-12-31T20:05:00Z');
    expect(parts.day).toBe('31');
    expect(parts.month).toBe('DEC');
    expect(parts.weekday).toBe('THU');
    expect(parts.year).toBe('2026');
    expect(parts.time).toBe('9:05 pm');
  });

  it('formats countdowns and holds with clamping at zero', () => {
    expect(formatCountdown(3_723_000)).toBe('01:02:03');
    expect(formatCountdown(65_000, false)).toBe('01:05');
    expect(formatHold(65_000)).toBe('1:05');
    expect(formatHold(-1)).toBe('0:00');
  });

  it('detects past dates and builds initials', () => {
    expect(isPast('2000-01-01T00:00:00Z')).toBe(true);
    expect(isPast('2999-01-01T00:00:00Z')).toBe(false);
    expect(initials('Ada  Lovelace Byron')).toBe('AL');
    expect(initials('')).toBe('');
  });
});

describe('status labels and role homes', () => {
  it('maps every event status to a label and class', () => {
    expect(eventStatusLabel('PendingApproval')).toBe('In review');
    expect(eventStatusLabel('Published')).toBe('Published');
    expect(eventStatusLabel('Draft')).toBe('Draft');
    expect(eventStatusClass('PendingApproval')).toBe('s-pending');
    expect(eventStatusClass('Suspended')).toBe('s-suspended');
    expect(eventStatusClass('Approved')).toBe('s-approved');
  });

  it('maps reservation labels and role landing pages', () => {
    expect(reservationStatusLabel('Held')).toBe('Held');
    expect(reservationStatusLabel('Cancelled')).toBe('Cancelled');
    expect(roleHome('Organizer')).toBe('/organizer');
    expect(roleHome('Admin')).toBe('/admin');
    expect(roleHome('Attendee')).toBe('/');
    expect(roleHome(null)).toBe('/');
  });
});