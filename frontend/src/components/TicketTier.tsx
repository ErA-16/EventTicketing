import type { TicketType } from '../api/types';
import { useNow } from '../lib/hooks';
import { formatDateTime, formatMoney, formatNumber } from '../lib/format';

export function TicketTier({
  tier,
  disabled = false,
  busy = false,
  buyLabel,
  onBuy,
  holdLabel,
  onHold,
}: {
  tier: TicketType;
  disabled?: boolean;
  busy?: boolean;
  buyLabel?: string;
  onBuy: () => void;
  holdLabel?: string;
  onHold?: () => void;
}) {
  const now = useNow(30_000);
  const salesOpen = new Date(tier.salesOpenAt).getTime() <= now;
  const earlyBirdActive =
    tier.earlyBirdPrice != null && tier.currentPrice < tier.price;
  const soldOut = tier.remaining <= 0;
  const soldPercent = tier.quantity > 0 ? (tier.sold / tier.quantity) * 100 : 0;
  const heldPercent = tier.quantity > 0 ? (tier.held / tier.quantity) * 100 : 0;
  const lowStock = !soldOut && tier.remaining <= Math.max(5, tier.quantity * 0.1);

  let label = buyLabel ?? 'Buy now';
  if (soldOut) {
    label = 'Sold out';
  } else if (!salesOpen) {
    label = `Sales open ${formatDateTime(tier.salesOpenAt)}`;
  }

  return (
    <div className="tier-wrap">
      <article className={`tier${soldOut ? ' out' : ''}`}>
        <div className="tier-top">
          <span className="mono">Ticket type</span>
          {earlyBirdActive ? (
            <span className="sticker">Early bird</span>
          ) : lowStock ? (
            <span className="sticker pink">Low stock</span>
          ) : null}
          <h3>{tier.name}</h3>
          <p>One person, one entry.</p>
        </div>
        <div className="tier-body">
          <div className="price">
            {formatMoney(tier.currentPrice)}
            {earlyBirdActive ? <s>{formatMoney(tier.price)}</s> : null}
          </div>
          <div className="per">per ticket · no booking fee</div>
          {earlyBirdActive && tier.earlyBirdEndsAt ? (
            <p className="eb">
              Early bird ends {formatDateTime(tier.earlyBirdEndsAt)}
            </p>
          ) : null}
          <div className="stock">
            <div className="bar" aria-hidden="true">
              <i className="sold" style={{ width: `${Math.min(100, soldPercent)}%` }} />
              <i className="held" style={{ width: `${Math.min(100, heldPercent)}%` }} />
            </div>
            <span>
              {formatNumber(tier.remaining)} of {formatNumber(tier.quantity)} left
            </span>
            <div className="legend">
              <span>{formatNumber(tier.sold)} sold</span>
              <span>{formatNumber(tier.held)} held</span>
              <span>{formatNumber(tier.remaining)} remaining</span>
            </div>
          </div>
          <div className="tier-actions">
            <button
              type="button"
              className="btn btn-ink"
              disabled={disabled || busy || soldOut || !salesOpen}
              onClick={onBuy}
            >
              {busy ? 'Working...' : label}
            </button>
            {onHold ? (
              <button
                type="button"
                className="tier-alt"
                disabled={disabled || busy || soldOut || !salesOpen}
                onClick={onHold}
              >
                {busy ? 'Working...' : (holdLabel ?? 'Hold 10 min')}
              </button>
            ) : null}
          </div>
        </div>
      </article>
    </div>
  );
}
