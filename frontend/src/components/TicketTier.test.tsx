import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { TicketType } from '../api/types';
import { TicketTier } from './TicketTier';

const tier: TicketType = {
  id: 'tier-1',
  name: 'General',
  price: 4000,
  earlyBirdPrice: 3000,
  earlyBirdEndsAt: '2026-12-01T20:00:00Z',
  currentPrice: 3000,
  quantity: 10,
  sold: 2,
  held: 1,
  remaining: 7,
  salesOpenAt: '2026-01-01T00:00:00Z',
};

describe('TicketTier', () => {
  it('renders stock, early-bird pricing and both actions', async () => {
    const user = userEvent.setup();
    const onBuy = vi.fn();
    const onHold = vi.fn();
    render(<TicketTier tier={tier} onBuy={onBuy} onHold={onHold} />);

    expect(screen.getByText('Early bird')).toBeInTheDocument();
    expect(screen.getByText('7 of 10 left')).toBeInTheDocument();
    expect(screen.getByText('2 sold')).toBeInTheDocument();
    expect(screen.getByText('1 held')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Buy now' }));
    await user.click(screen.getByRole('button', { name: 'Hold 10 min' }));
    expect(onBuy).toHaveBeenCalledOnce();
    expect(onHold).toHaveBeenCalledOnce();
  });

  it('disables both actions when the tier is sold out', () => {
    render(
      <TicketTier
        tier={{ ...tier, sold: 10, held: 0, remaining: 0 }}
        onBuy={vi.fn()}
        onHold={vi.fn()}
      />,
    );

    expect(screen.getByRole('button', { name: 'Sold out' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Hold 10 min' })).toBeDisabled();
  });

  it('disables purchase until sales open and shows the opening time', () => {
    const future = new Date(Date.now() + 3 * 60 * 60 * 1000).toISOString();
    render(
      <TicketTier
        tier={{ ...tier, salesOpenAt: future }}
        onBuy={vi.fn()}
        onHold={vi.fn()}
      />,
    );

    const button = screen.getByRole('button', { name: /Sales open/ });
    expect(button).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Hold 10 min' })).toBeDisabled();
  });

  it('shows a working state and disables actions while busy', () => {
    render(<TicketTier tier={tier} busy onBuy={vi.fn()} onHold={vi.fn()} />);
    expect(screen.getAllByRole('button', { name: 'Working...' })).toHaveLength(2);
    for (const button of screen.getAllByRole('button', { name: 'Working...' })) {
      expect(button).toBeDisabled();
    }
  });

  it('respects an external disabled flag and custom action labels', () => {
    render(
      <TicketTier
        tier={tier}
        disabled
        buyLabel="Reserve"
        holdLabel="Hold for 10 minutes"
        onBuy={vi.fn()}
        onHold={vi.fn()}
      />,
    );

    expect(screen.getByRole('button', { name: 'Reserve' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Hold for 10 minutes' })).toBeDisabled();
  });
});