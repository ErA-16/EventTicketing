import { StarIcon } from './Icons';

const PHRASES = [
  'LIVE MUSIC — LAGOS',
  'ABUJA',
  'PORT HARCOURT',
  'NO PRINTING — JUST SHOW YOUR CODE',
  'EVERY TICKET COUNTS',
  '18+',
];

function MarqueeHalf({ hidden = false }: { hidden?: boolean }) {
  return (
    <div className="marquee-half" aria-hidden={hidden || undefined}>
      {PHRASES.map((phrase) => (
        <span key={phrase} style={{ display: 'inline-flex', alignItems: 'center', gap: 10 }}>
          <StarIcon size={12} />
          {phrase}
        </span>
      ))}
    </div>
  );
}

export function Marquee() {
  return (
    <div className="marquee">
      <div className="marquee-track">
        <MarqueeHalf />
        <MarqueeHalf hidden />
      </div>
    </div>
  );
}
