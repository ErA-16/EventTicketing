import { useCallback, useState } from 'react';

const KEY = 'eventticketing.favorites.v1';

function readFavorites(): string[] {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) {
      return [];
    }

    const parsed = JSON.parse(raw) as unknown;
    return Array.isArray(parsed)
      ? parsed.filter((value): value is string => typeof value === 'string')
      : [];
  } catch {
    return [];
  }
}

function writeFavorites(ids: string[]): void {
  try {
    localStorage.setItem(KEY, JSON.stringify(ids));
  } catch {
    // Favourites are a convenience; ignore storage failures.
  }
}

export function useFavorite(eventId: string): [boolean, () => void] {
  const [saved, setSaved] = useState(() => readFavorites().includes(eventId));

  const toggle = useCallback(() => {
    const current = readFavorites();
    const next = current.includes(eventId)
      ? current.filter((id) => id !== eventId)
      : [...current, eventId];
    writeFavorites(next);
    setSaved(next.includes(eventId));
  }, [eventId]);

  return [saved, toggle];
}
