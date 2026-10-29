import { describe, expect, it } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useFavorite } from './favorites';

describe('useFavorite', () => {
  it('starts unsaved, persists toggles and restores them in a new hook', () => {
    const first = renderHook(() => useFavorite('event-1'));
    expect(first.result.current[0]).toBe(false);

    act(() => first.result.current[1]());
    expect(first.result.current[0]).toBe(true);
    expect(localStorage.getItem('eventticketing.favorites.v1')).toBe('["event-1"]');

    const second = renderHook(() => useFavorite('event-1'));
    expect(second.result.current[0]).toBe(true);
  });

  it('toggles the same event off without affecting other ids', () => {
    localStorage.setItem('eventticketing.favorites.v1', '["event-1","event-2"]');
    const { result } = renderHook(() => useFavorite('event-1'));

    act(() => result.current[1]());

    expect(result.current[0]).toBe(false);
    expect(localStorage.getItem('eventticketing.favorites.v1')).toBe('["event-2"]');
  });

  it('ignores malformed stored values', () => {
    localStorage.setItem('eventticketing.favorites.v1', '{"not":"an array"}');
    const { result } = renderHook(() => useFavorite('event-1'));
    expect(result.current[0]).toBe(false);
  });
});