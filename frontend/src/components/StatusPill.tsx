import type { EventStatus } from '../api/types';
import { eventStatusClass, eventStatusLabel } from '../lib/format';

export function StatusPill({ status }: { status: EventStatus }) {
  return (
    <span className={`status ${eventStatusClass(status)}`}>
      {eventStatusLabel(status)}
    </span>
  );
}
