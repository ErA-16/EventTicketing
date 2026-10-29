import { useCallback, useEffect, useState } from 'react';
import { apiRequest, errorMessage } from '../../api/client';
import type { Wallet } from '../../api/types';
import { ErrorState, Loading } from '../../components/Feedback';
import { useToast } from '../../context/ToastContext';
import { formatDateTime, formatMoney } from '../../lib/format';

export function WalletPage() {
  const toast = useToast();
  const [wallet, setWallet] = useState<Wallet | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [funding, setFunding] = useState<number | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setWallet(await apiRequest<Wallet>('/wallet'));
    } catch (caught) {
      setError(errorMessage(caught, 'Could not load your wallet.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const fund = async (amount: number) => {
    setFunding(amount);
    try {
      const updated = await apiRequest<Wallet>('/wallet/fund', {
        method: 'POST',
        body: { amount },
      });
      setWallet(updated);
      toast.success(`${formatMoney(amount)} added to your wallet.`);
    } catch (caught) {
      toast.error(errorMessage(caught));
    } finally {
      setFunding(null);
    }
  };

  if (loading) {
    return <Loading label="Opening wallet" />;
  }

  if (error || !wallet) {
    return (
      <div className="wrap sec">
        <ErrorState
          message={error ?? 'Could not load your wallet.'}
          onRetry={() => void load()}
        />
      </div>
    );
  }

  return (
    <div className="wrap sec">
      <div className="page-head">
        <div>
          <span className="mono muted" style={{ fontSize: '.7rem' }}>
            Money ready for the gate
          </span>
          <h1 className="h2">Wallet</h1>
        </div>
      </div>

      <div className="co">
        <div className="panel">
          <span className="mono muted" style={{ fontSize: '.68rem' }}>
            Available balance
          </span>
          <div className="balance">{formatMoney(wallet.balance)}</div>
          <div className="fund">
            <button
              type="button"
              className="btn btn-lime"
              disabled={funding !== null}
              onClick={() => void fund(5000)}
            >
              {funding === 5000 ? 'Adding…' : 'Add ₦5,000'}
            </button>
            <button
              type="button"
              className="btn"
              disabled={funding !== null}
              onClick={() => void fund(10000)}
            >
              {funding === 10000 ? 'Adding…' : 'Add ₦10,000'}
            </button>
          </div>
          <p className="fine">
            This is a demo wallet for the project. Funding is capped at ₦10,000 per
            request. Withdrawals and transfers are not available.
          </p>
        </div>

        <div className="panel">
          <div className="panel-head">
            <h2 className="h3">Recent activity</h2>
            <span className="status s-draft">Latest 20</span>
          </div>

          {wallet.transactions.length === 0 ? (
            <div className="note">
              No wallet activity yet. Add funds, then pay for a ticket to see the
              ledger fill up.
            </div>
          ) : (
            <div>
              {wallet.transactions.map((transaction) => (
                <div className="tx" key={transaction.id}>
                  <div>
                    <b>
                      {transaction.type === 'Fund'
                        ? 'Wallet top-up'
                        : 'Ticket purchase'}
                    </b>
                    <small>
                      {transaction.type === 'Fund' ? 'Fund' : 'Purchase'} ·{' '}
                      {transaction.reference}
                    </small>
                    <small style={{ display: 'block', marginTop: 2 }}>
                      {formatDateTime(transaction.createdAt)}
                    </small>
                  </div>
                  <div
                    className={`amt ${
                      transaction.amount >= 0 ? 'cr' : 'dr'
                    }`}
                  >
                    {transaction.amount >= 0 ? '+' : ''}
                    {formatMoney(transaction.amount)}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
