export type Role = 'Attendee' | 'Organizer' | 'Admin';

export type ReservationStatus = 'Held' | 'Paid' | 'Expired' | 'Cancelled';

export type EventStatus =
  | 'Draft'
  | 'PendingApproval'
  | 'Approved'
  | 'Suspended'
  | 'Published';

export type WalletTransactionType = 'Fund' | 'Purchase';

export interface AuthResponse {
  token: string;
  refreshToken: string;
  expiresIn: number;
}

export interface RefreshResponse {
  token: string;
  expiresIn: number;
}

export interface AuthUser {
  id: string;
  email: string;
  name: string;
  role: Role;
}

export interface EventSummary {
  id: string;
  title: string;
  venue: string;
  startsAt: string;
  imageUrl: string | null;
  minPrice: number;
  ticketsLeft: number;
}

export interface TicketType {
  id: string;
  name: string;
  price: number;
  earlyBirdPrice: number | null;
  earlyBirdEndsAt: string | null;
  currentPrice: number;
  quantity: number;
  sold: number;
  held: number;
  remaining: number;
  salesOpenAt: string;
}

export interface EventDetails {
  id: string;
  title: string;
  description: string;
  venue: string;
  startsAt: string;
  imageUrl: string | null;
  organizerName: string;
  ticketTypes: TicketType[];
}

export interface EventResponse {
  id: string;
  organizerId: string;
  organizerName: string;
  title: string;
  description: string;
  venue: string;
  startsAt: string;
  imagePath: string | null;
  imageUrl: string | null;
  status: EventStatus;
  rejectionReason: string | null;
  ticketsLeft: number;
  ticketTypes: TicketType[];
}

export interface Reservation {
  id: string;
  eventId: string;
  ticketTypeId: string;
  priceAtReservation: number;
  status: ReservationStatus;
  createdAt: string;
  expiresAt: string;
}

export interface MyReservation {
  id: string;
  eventId: string;
  eventTitle: string;
  venue: string;
  startsAt: string;
  ticketTypeId: string;
  ticketTypeName: string;
  status: ReservationStatus;
  priceAtReservation: number;
  createdAt: string;
  expiresAt: string;
  paidAt: string | null;
  ticketId: string | null;
  ticketCode: string | null;
  checkedInAt: string | null;
  isExpired: boolean;
}

export interface PaymentResponse {
  reservationId: string;
  eventId: string;
  status: ReservationStatus;
  pricePaid: number;
  ticketId: string;
  code: string;
}

export interface MyTicket {
  ticketId: string;
  code: string;
  eventId: string;
  eventTitle: string;
  organizerName: string;
  venue: string;
  startsAt: string;
  ticketTypeName: string;
  pricePaid: number;
  paidAt: string | null;
  checkedInAt: string | null;
}

export interface WalletTransaction {
  id: string;
  type: WalletTransactionType;
  reference: string;
  amount: number;
  createdAt: string;
}

export interface Wallet {
  balance: number;
  transactions: WalletTransaction[];
}

export interface CheckInResponse {
  ticketId: string;
  code: string;
  eventId: string;
  eventTitle: string;
  checkedInAt: string;
}

export interface ImageUploadResponse {
  imagePath: string;
  imageUrl: string;
}

export interface AdminOrganizer {
  id: string;
  firstName: string;
  lastName: string;
  organizationName: string | null;
  email: string;
  isSuspended: boolean;
}

export interface AdminUser {
  id: string;
  firstName: string;
  lastName: string;
  organizationName: string | null;
  displayName: string;
  email: string;
  roles: string[];
  isSuspended: boolean;
}

export interface CursorPage<T> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
}

export interface EventInput {
  title: string;
  description: string;
  venue: string;
  startsAt: string;
}

export interface TicketTypeInput {
  name: string;
  price: number;
  quantity: number;
  salesOpenAt: string;
  earlyBirdPrice: number | null;
  earlyBirdEndsAt: string | null;
}
