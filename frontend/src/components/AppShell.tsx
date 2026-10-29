import { useEffect, useRef, useState } from 'react';
import { Link, NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { HoldStrip } from './HoldStrip';
import { Marquee } from './Marquee';
import { TicketIcon, WalletIcon } from './Icons';

export function AppShell() {
  const { user, role, isAuthenticated, logout } = useAuth();
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  const menuButtonRef = useRef<HTMLButtonElement>(null);

  const closeMenu = () => {
    setMenuOpen(false);
    menuButtonRef.current?.focus();
  };

  useEffect(() => {
    const closeOnHistory = () => setMenuOpen(false);
    window.addEventListener('popstate', closeOnHistory);
    return () => window.removeEventListener('popstate', closeOnHistory);
  }, []);

  useEffect(() => {
    if (!menuOpen) {
      return;
    }

    menuRef.current?.querySelector<HTMLElement>('a, button')?.focus();

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setMenuOpen(false);
        menuButtonRef.current?.focus();
      }
    };

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [menuOpen]);

  const handleLogout = () => {
    setMenuOpen(false);
    void logout();
  };

  return (
    <div className="app-shell">
      <Marquee />
      <header>
        <nav className="nav wrap" aria-label="Main">
          <Link
            className="logo"
            to={role === 'Organizer' ? '/organizer' : role === 'Admin' ? '/admin' : '/'}
          >
            <span className="logo-mark">
              <TicketIcon size={20} />
            </span>
            <span className="t">EventTicketing</span>
          </Link>
          <div className="nav-right">
            {!isAuthenticated ? (
              <>
                <NavLink className="chip" to="/" end>
                  Discover
                </NavLink>
                <NavLink className="chip" to="/login">
                  Log in
                </NavLink>
                <Link className="btn btn-lime btn-sm" to="/signup">
                  Sign up
                </Link>
              </>
            ) : null}

            {role === 'Attendee' ? (
              <>
                <NavLink className="chip" to="/" end>
                  Discover
                </NavLink>
                <NavLink className="chip" to="/my-tickets">
                  My tickets
                </NavLink>
                <NavLink className="chip" to="/wallet">
                  <WalletIcon />
                  Wallet
                </NavLink>
                <button type="button" className="chip" onClick={() => void logout()}>
                  Log out
                </button>
              </>
            ) : null}

            {role === 'Organizer' ? (
              <>
                <NavLink className="chip" to="/organizer" end>
                  My events
                </NavLink>
                <NavLink className="chip" to="/organizer/check-in">
                  Check in
                </NavLink>
                <span className="chip static">{user?.name}</span>
                <button type="button" className="chip" onClick={() => void logout()}>
                  Log out
                </button>
              </>
            ) : null}

            {role === 'Admin' ? (
              <>
                <NavLink className="chip" to="/admin" end>
                  Review
                </NavLink>
                <NavLink className="chip" to="/admin/events">
                  Events
                </NavLink>
                <NavLink className="chip" to="/admin/organizers">
                  Organizers
                </NavLink>
                <NavLink className="chip" to="/admin/users">
                  Users
                </NavLink>
                <button type="button" className="chip" onClick={() => void logout()}>
                  Log out
                </button>
              </>
            ) : null}
          </div>

          <button
            ref={menuButtonRef}
            type="button"
            className="chip menu-toggle"
            aria-label="Navigation menu"
            aria-expanded={menuOpen}
            aria-controls="mobile-menu"
            onClick={() => (menuOpen ? closeMenu() : setMenuOpen(true))}
          >
            {menuOpen ? 'Close' : 'Menu'}
          </button>

          {menuOpen ? (
            <>
              <button
                type="button"
                className="menu-scrim"
                tabIndex={-1}
                aria-label="Close menu"
                onClick={closeMenu}
              />
              <div
                className="mobile-menu"
                id="mobile-menu"
                ref={menuRef}
                aria-label="Mobile navigation"
              >
                {!isAuthenticated ? (
                  <>
                    <NavLink
                      className="menu-link"
                      to="/"
                      end
                      onClick={() => setMenuOpen(false)}
                    >
                      Discover
                    </NavLink>
                    <NavLink
                      className="menu-link"
                      to="/login"
                      onClick={() => setMenuOpen(false)}
                    >
                      Log in
                    </NavLink>
                    <NavLink
                      className="menu-link"
                      to="/signup"
                      onClick={() => setMenuOpen(false)}
                    >
                      Sign up
                    </NavLink>
                  </>
                ) : null}

                {role === 'Attendee' ? (
                  <>
                    <NavLink
                      className="menu-link"
                      to="/"
                      end
                      onClick={() => setMenuOpen(false)}
                    >
                      Discover
                    </NavLink>
                    <NavLink
                      className="menu-link"
                      to="/my-tickets"
                      onClick={() => setMenuOpen(false)}
                    >
                      My tickets
                    </NavLink>
                    <NavLink
                      className="menu-link"
                      to="/wallet"
                      onClick={() => setMenuOpen(false)}
                    >
                      Wallet
                    </NavLink>
                    <button
                      type="button"
                      className="menu-link"
                      onClick={handleLogout}
                    >
                      Log out
                    </button>
                  </>
                ) : null}

                {role === 'Organizer' ? (
                  <>
                    <NavLink
                      className="menu-link"
                      to="/organizer"
                      end
                      onClick={() => setMenuOpen(false)}
                    >
                      My events
                    </NavLink>
                    <NavLink
                      className="menu-link"
                      to="/organizer/check-in"
                      onClick={() => setMenuOpen(false)}
                    >
                      Check in
                    </NavLink>
                    <span className="menu-account">{user?.name}</span>
                    <button
                      type="button"
                      className="menu-link"
                      onClick={handleLogout}
                    >
                      Log out
                    </button>
                  </>
                ) : null}

                {role === 'Admin' ? (
                  <>
                    <NavLink
                      className="menu-link"
                      to="/admin"
                      end
                      onClick={() => setMenuOpen(false)}
                    >
                      Review
                    </NavLink>
                    <NavLink
                      className="menu-link"
                      to="/admin/events"
                      onClick={() => setMenuOpen(false)}
                    >
                      Events
                    </NavLink>
                    <NavLink
                      className="menu-link"
                      to="/admin/organizers"
                      onClick={() => setMenuOpen(false)}
                    >
                      Organizers
                    </NavLink>
                    <NavLink
                      className="menu-link"
                      to="/admin/users"
                      onClick={() => setMenuOpen(false)}
                    >
                      Users
                    </NavLink>
                    <button
                      type="button"
                      className="menu-link"
                      onClick={handleLogout}
                    >
                      Log out
                    </button>
                  </>
                ) : null}
              </div>
            </>
          ) : null}
        </nav>
      </header>

      <HoldStrip />

      <main className="app-main">
        <Outlet />
      </main>

      <footer className="foot">
        <div className="wrap">
          <div className="foot-mark">EVENTTICKETING</div>
          <p>Find your next night out. One code, one scan, no printing.</p>
        </div>
      </footer>
    </div>
  );
}
