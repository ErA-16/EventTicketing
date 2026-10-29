import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { roleHome } from '../lib/format';

export function NotFoundPage() {
  const { role } = useAuth();

  return (
    <div className="wrap sec">
      <div className="empty">
        <span className="sticker pink">404</span>
        <h1 className="h2">Wrong gate.</h1>
        <p>This page does not exist. The show is happening somewhere else.</p>
        <Link className="btn" to={roleHome(role)}>
          Back to the main stage
        </Link>
      </div>
    </div>
  );
}
