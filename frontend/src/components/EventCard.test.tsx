import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { EventSummary } from '../api/types';
import { EventCard } from './EventCard';

const event: EventSummary = {
  id: 'event-1',
  title: 'Lagos Nights',
  venue: 'Eko Convention Centre',
  startsAt: '2026-12-31T20:00:00Z',
  imageUrl: null,
  minPrice: 4000,
  ticketsLeft: 7,
};

function renderCard() {
  return render(
    <MemoryRouter initialEntries={['/']}>
      <Routes>
        <Route path="/" element={<EventCard event={event} />} />
        <Route path="/events/:eventId" element={<div>Event detail</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('EventCard', () => {
  it('shows event details and navigates on click', async () => {
    const user = userEvent.setup();
    renderCard();

    expect(screen.getByText('Lagos Nights')).toBeInTheDocument();
    expect(screen.getByText('Eko Convention Centre')).toBeInTheDocument();
    expect(screen.getByText('7 tickets left')).toBeInTheDocument();
    expect(screen.getByText(/From/)).toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: /Lagos Nights at Eko Convention Centre/ }));
    expect(screen.getByText('Event detail')).toBeInTheDocument();
  });

  it('toggles the favourite without navigating', async () => {
    const user = userEvent.setup();
    renderCard();

    const save = screen.getByRole('button', { name: 'Save event' });
    expect(save).toHaveAttribute('aria-pressed', 'false');

    await user.click(save);

    expect(screen.getByRole('button', { name: 'Remove from saved events' }))
      .toHaveAttribute('aria-pressed', 'true');
    expect(screen.queryByText('Event detail')).not.toBeInTheDocument();
    expect(localStorage.getItem('eventticketing.favorites.v1')).toContain('event-1');
  });

  it('shows sold out instead of a price when no tickets remain', () => {
    render(
      <MemoryRouter>
        <EventCard event={{ ...event, ticketsLeft: 0, minPrice: 0 }} />
      </MemoryRouter>,
    );

    expect(screen.getByText('Sold out')).toBeInTheDocument();
    expect(screen.getByText('Free')).toBeInTheDocument();
  });

  it('does not navigate when pressing an unrelated key', async () => {
    const user = userEvent.setup();
    renderCard();

    const card = screen.getByRole('link', { name: /Lagos Nights at Eko Convention Centre/ });
    card.focus();
    await user.keyboard('a');
    expect(screen.queryByText('Event detail')).not.toBeInTheDocument();
  });
});