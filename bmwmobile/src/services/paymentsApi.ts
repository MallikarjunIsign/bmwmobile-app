import { http, SLOW_REQUEST, toServerError } from './api';
import type { Address, DeliveryOption, Order, OrderPayout } from '../types/listing';

/** Not available in this country ("coming soon"), not started, being verified, active, or Stripe needs more. */
export type SellerPaymentStatus = 'NotAvailable' | 'NotStarted' | 'Pending' | 'Active' | 'ActionNeeded';

export interface SellerPaymentAccount {
  /** Online payment works in the seller's country (USA, Ireland). */
  available: boolean;
  country?: string | null;
  status: SellerPaymentStatus;
  message: string;
  /** USA / Ireland: only an Active seller can publish listings. */
  canPublish: boolean;
  requirementsDue: number;
}

export interface PaymentConfig {
  enabled: boolean;
  publishableKey?: string | null;
  countries: string[];
  commissionPercent: number;
}

/** What the payment sheet needs, and the amounts the backend calculated – in CENTS. */
export interface CheckoutSession {
  paymentId: string;
  clientSecret: string;
  customerId: string;
  ephemeralKey: string;
  publishableKey?: string | null;
  currency: string;
  itemAmount: number;
  shippingAmount: number;
  totalAmount: number;
  /** Buy now: the quantity is held for you until then. */
  reservedUntil?: string | null;
  orderId: string;
  merchantDisplayName: string;
}

export interface PaymentStatus {
  paymentId: string;
  status: 'Pending' | 'Succeeded' | 'Failed' | 'Canceled' | 'Refunded' | 'PartiallyRefunded' | 'Disputed';
  orderId?: string | null;
  failureMessage?: string | null;
  reservedUntil?: string | null;
}

/** "The winner didn't pay – it's yours for your bid". */
export interface SecondChanceOffer {
  id: string;
  listingId: string;
  title: string;
  amount: number;
  currency?: string | null;
  expiresAt: string;
  round: number;
}

/** Pickup, or Shipping with the address (book entry or new) and the delivery location. */
export interface DeliveryRequest {
  method: DeliveryOption;
  addressId?: string;
  address?: Address;
  latitude?: number;
  longitude?: number;
  saveAddress?: boolean;
}

async function call<T>(request: () => Promise<{ data: T }>, fallback: string): Promise<T> {
  try {
    return (await request()).data;
  } catch (e) {
    throw toServerError(e, fallback);
  }
}

/** Online payments (Stripe, test mode): sellers' payment accounts, and buyers paying. */
export const PaymentsApi = {
  config: () => call<PaymentConfig>(() => http.get('/payments/config', SLOW_REQUEST), "Couldn't load the payment settings"),

  // ── sellers ──
  /** The seller's status (read again from Stripe). */
  sellerAccount: () =>
    call<SellerPaymentAccount>(() => http.get('/payments/seller-account', SLOW_REQUEST), "Couldn't load your payment setup"),

  /** First visit: opens the seller's payment account (USA / Ireland) when it doesn't exist yet. */
  ensureSellerAccount: () =>
    call<SellerPaymentAccount>(() => http.post('/payments/seller-account', undefined, SLOW_REQUEST), "Couldn't start your payment setup"),

  /** Stripe's hosted onboarding (opened in the browser; it comes back to the app). */
  onboardingLink: () =>
    call<{ url: string }>(() => http.post('/payments/seller-account/onboarding-link', undefined, SLOW_REQUEST), "Couldn't open the payment setup").then(r => r.url),

  /** Stripe's Express dashboard: payouts and bank account. */
  dashboardLink: () =>
    call<{ url: string }>(() => http.post('/payments/seller-account/dashboard-link', undefined, SLOW_REQUEST), "Couldn't open your payouts").then(r => r.url),

  // ── buyers ──
  /** Buy now: reserves the quantity (15 minutes) and returns the payment sheet's details and the total. */
  buyNow: (listingId: string, quantity: number, delivery: DeliveryRequest) =>
    call<CheckoutSession>(() => http.post('/payments/checkout/buy-now', { listingId, quantity, delivery }, SLOW_REQUEST), "Couldn't start the payment"),

  /** Pay for an order waiting for payment (auction win / accepted second-chance offer). */
  payOrder: (orderId: string) =>
    call<CheckoutSession>(() => http.post(`/payments/orders/${orderId}/pay`, undefined, SLOW_REQUEST), "Couldn't start the payment"),

  /** The sheet was closed or failed: the reservation is released at once. */
  cancelPayment: (paymentId: string) =>
    call<unknown>(() => http.post(`/payments/${paymentId}/cancel`, undefined, SLOW_REQUEST), "Couldn't cancel the payment"),

  /** Paid once Stripe's webhook arrived (the order is created / starts then). */
  paymentStatus: (paymentId: string) => call<PaymentStatus>(() => http.get(`/payments/${paymentId}`, SLOW_REQUEST), "Couldn't check the payment"),

  offers: () => call<SecondChanceOffer[]>(() => http.get('/payments/offers', SLOW_REQUEST), "Couldn't load your offers"),
  acceptOffer: (id: string) => call<Order>(() => http.post(`/payments/offers/${id}/accept`, undefined, SLOW_REQUEST), "Couldn't accept the offer"),
  declineOffer: (id: string) => call<unknown>(() => http.post(`/payments/offers/${id}/decline`, undefined, SLOW_REQUEST), "Couldn't decline the offer"),

  // ── after paying ──
  /** Buyer: the receipt; seller: the payout statement. */
  receipt: (orderId: string) => call<Receipt>(() => http.get(`/payments/orders/${orderId}/receipt`, SLOW_REQUEST), "Couldn't load the receipt"),
  /** Buyer: puts the order on hold (no automatic completion, the seller isn't paid) until it's sorted out. */
  reportProblem: (orderId: string, text: string) =>
    call<Order>(() => http.post(`/payments/orders/${orderId}/problem`, { text }, SLOW_REQUEST), "Couldn't report the problem"),
  /** Seller, before pickup / shipping: cancels the order and refunds the buyer in full. */
  cancelAndRefund: (orderId: string, text: string) =>
    call<Order>(() => http.post(`/payments/orders/${orderId}/cancel-and-refund`, { text }, SLOW_REQUEST), "Couldn't cancel the order"),
};

/** The order's receipt (buyer) / payout statement (seller); amounts in cents. */
export interface Receipt {
  orderId: string;
  role: 'buyer' | 'seller';
  title: string;
  quantity: number;
  currency: string;
  itemAmount: number;
  shippingAmount: number;
  totalAmount: number;
  paidAt?: string | null;
  card?: string | null;
  receiptUrl?: string | null;
  refundedAmount: number;
  refunds: { amount: number; reason: string; status: string; createdAt: string }[];
  commissionPercent: number;
  commissionAmount?: number | null;
  payout?: OrderPayout | null;
  disputeStatus?: string | null;
}

/** The status line and the button the seller sees. */
export function sellerPaymentView(a: SellerPaymentAccount | null): { label: string; tone: 'success' | 'warning' | 'muted' | 'danger'; action: string | null } {
  switch (a?.status) {
    case 'Active':
      return { label: 'Active', tone: 'success', action: null };
    case 'Pending':
      return { label: 'Pending verification', tone: 'warning', action: null };
    case 'ActionNeeded':
      return { label: 'Action needed', tone: 'danger', action: 'CONTINUE SETUP' };
    case 'NotAvailable':
      return { label: 'Coming soon', tone: 'muted', action: null };
    default:
      return { label: 'Not started', tone: 'muted', action: 'SET UP PAYMENTS' };
  }
}

/** USA / Ireland pay online now; India / Nigeria later. */
export const ONLINE_PAYMENT_COUNTRIES = ['US', 'IE'];

/** Cents → the money text: "$340", "$12.50". */
export function centsText(cents: number, money: (n: number, decimals?: number) => string): string {
  return cents % 100 === 0 ? money(cents / 100) : money(cents / 100, 2);
}

/** "Item $340 + Shipping $20 = Total $360" (pickup: "Item $340 = Total $340 (pickup, free)"). */
export function checkoutTotalText(s: Pick<CheckoutSession, 'itemAmount' | 'shippingAmount' | 'totalAmount'>, money: (n: number, decimals?: number) => string): string {
  return s.shippingAmount > 0
    ? `Item ${centsText(s.itemAmount, money)} + Shipping ${centsText(s.shippingAmount, money)} = Total ${centsText(s.totalAmount, money)}`
    : `Item ${centsText(s.itemAmount, money)} = Total ${centsText(s.totalAmount, money)} (pickup, free)`;
}

/** Time left to pay / for the reservation: "31 h 5 min", "14:32" (under an hour), "Time is up". */
export function timeLeftText(until: string | Date, now: number = Date.now()): string {
  const ms = new Date(until).getTime() - now;
  if (ms <= 0) return 'Time is up';
  const totalSeconds = Math.floor(ms / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  if (hours > 0) return `${hours} h ${minutes} min`;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}
