import { useEffect } from 'react';
import {
  BrowserRouter,
  Route,
  Routes,
  useLocation,
} from 'react-router-dom';
import { AppShell } from './components/AppShell';
import { RequireRole } from './components/RequireAuth';
import { AuthProvider } from './context/AuthContext';
import { ToastProvider } from './context/ToastContext';
import { AuthPage } from './pages/AuthPage';
import { NotFoundPage } from './pages/NotFoundPage';
import { AdminEventsPage } from './pages/admin/AdminEventsPage';
import { AdminOrganizersPage } from './pages/admin/AdminOrganizersPage';
import { AdminReviewPage } from './pages/admin/AdminReviewPage';
import { AdminUsersPage } from './pages/admin/AdminUsersPage';
import { CheckoutPage } from './pages/attendee/CheckoutPage';
import { EventPage } from './pages/attendee/EventPage';
import { HomePage } from './pages/attendee/HomePage';
import { MyTicketsPage } from './pages/attendee/MyTicketsPage';
import { WalletPage } from './pages/attendee/WalletPage';
import { CheckInPage } from './pages/organizer/CheckInPage';
import { OrganizerEventFormPage } from './pages/organizer/OrganizerEventFormPage';
import { OrganizerEventsPage } from './pages/organizer/OrganizerEventsPage';
import { OrganizerManagePage } from './pages/organizer/OrganizerManagePage';

function ScrollToTop() {
  const location = useLocation();

  useEffect(() => {
    window.scrollTo(0, 0);
  }, [location.pathname]);

  return null;
}

function App() {
  return (
    <BrowserRouter>
      <ToastProvider>
        <AuthProvider>
          <ScrollToTop />
          <Routes>
            <Route element={<AppShell />}>
              <Route index element={<HomePage />} />
              <Route path="events/:id" element={<EventPage />} />
              <Route path="login" element={<AuthPage mode="login" />} />
              <Route path="signup" element={<AuthPage mode="signup" />} />

              <Route
                path="checkout/:id"
                element={
                  <RequireRole role="Attendee">
                    <CheckoutPage />
                  </RequireRole>
                }
              />
              <Route
                path="my-tickets"
                element={
                  <RequireRole role="Attendee">
                    <MyTicketsPage />
                  </RequireRole>
                }
              />
              <Route
                path="wallet"
                element={
                  <RequireRole role="Attendee">
                    <WalletPage />
                  </RequireRole>
                }
              />

              <Route
                path="organizer"
                element={
                  <RequireRole role="Organizer">
                    <OrganizerEventsPage />
                  </RequireRole>
                }
              />
              <Route
                path="organizer/check-in"
                element={
                  <RequireRole role="Organizer">
                    <CheckInPage />
                  </RequireRole>
                }
              />
              <Route
                path="organizer/events/new"
                element={
                  <RequireRole role="Organizer">
                    <OrganizerEventFormPage />
                  </RequireRole>
                }
              />
              <Route
                path="organizer/events/:id/edit"
                element={
                  <RequireRole role="Organizer">
                    <OrganizerEventFormPage />
                  </RequireRole>
                }
              />
              <Route
                path="organizer/events/:id"
                element={
                  <RequireRole role="Organizer">
                    <OrganizerManagePage />
                  </RequireRole>
                }
              />

              <Route
                path="admin"
                element={
                  <RequireRole role="Admin">
                    <AdminReviewPage />
                  </RequireRole>
                }
              />
              <Route
                path="admin/events"
                element={
                  <RequireRole role="Admin">
                    <AdminEventsPage />
                  </RequireRole>
                }
              />
              <Route
                path="admin/organizers"
                element={
                  <RequireRole role="Admin">
                    <AdminOrganizersPage />
                  </RequireRole>
                }
              />
              <Route
                path="admin/users"
                element={
                  <RequireRole role="Admin">
                    <AdminUsersPage />
                  </RequireRole>
                }
              />

              <Route path="*" element={<NotFoundPage />} />
            </Route>
          </Routes>
        </AuthProvider>
      </ToastProvider>
    </BrowserRouter>
  );
}

export default App;
