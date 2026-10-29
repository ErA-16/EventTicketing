import { useEffect, useState } from 'react';

export function useNow(intervalMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => window.clearInterval(id);
  }, [intervalMs]);

  return now;
}

export interface Countdown {
  days: number;
  hours: number;
  minutes: number;
  seconds: number;
  milliseconds: number;
  expired: boolean;
}

export function useCountdown(targetIso: string | null | undefined): Countdown {
  const now = useNow(1000);
  const target = targetIso ? new Date(targetIso).getTime() : 0;
  const milliseconds = Math.max(0, target - now);

  return {
    days: Math.floor(milliseconds / 86_400_000),
    hours: Math.floor((milliseconds % 86_400_000) / 3_600_000),
    minutes: Math.floor((milliseconds % 3_600_000) / 60_000),
    seconds: Math.floor((milliseconds % 60_000) / 1000),
    milliseconds,
    expired: !targetIso || milliseconds <= 0,
  };
}
