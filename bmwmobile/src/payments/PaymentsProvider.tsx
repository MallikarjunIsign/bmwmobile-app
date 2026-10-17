import React, { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { StripeProvider, useStripe } from '@stripe/stripe-react-native';
import { useAuth } from '../auth/AuthContext';
import { PaymentsApi, type CheckoutSession, type PaymentConfig } from '../services/paymentsApi';

const OFF: PaymentConfig = { enabled: false, publishableKey: null, countries: [], commissionPercent: 0 };
const ConfigContext = createContext<PaymentConfig>(OFF);

/** Whether online payment is on (server has Stripe keys) and in which countries (US, IE). */
export const usePaymentConfig = () => useContext(ConfigContext);

/** Online payment for a listing / order in this country? (India / Nigeria: "coming soon"). */
export function useOnlinePayment(countryCode?: string | null): boolean {
  const config = usePaymentConfig();
  return config.enabled && !!countryCode && config.countries.includes(countryCode.toUpperCase());
}

/**
 * Loads the payment settings after sign-in and sets up Stripe with the PUBLISHABLE key from the server (the secret key
 * never leaves the backend). Without online payment the app works as before.
 */
export const PaymentsProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { status } = useAuth();
  const [config, setConfig] = useState<PaymentConfig>(OFF);

  useEffect(() => {
    if (status !== 'signedIn') return;
    let alive = true;
    PaymentsApi.config().then(c => alive && setConfig(c)).catch(() => undefined);
    return () => {
      alive = false;
    };
  }, [status]);

  const content = <ConfigContext.Provider value={config}>{children}</ConfigContext.Provider>;
  if (!config.enabled || !config.publishableKey) return content;
  return (
    // urlScheme: back to the app after a bank's 3D Secure page (shopcarparts://…, AndroidManifest).
    <StripeProvider publishableKey={config.publishableKey} urlScheme="shopcarparts" merchantIdentifier="merchant.com.shopcarparts">
      {content}
    </StripeProvider>
  );
};

export type SheetResult =
  | { status: 'paid'; orderId: string }
  | { status: 'canceled' }
  | { status: 'failed'; message: string }
  /** Submitted, but Stripe's confirmation hasn't reached the server yet (the order shows it soon). */
  | { status: 'processing' };

/**
 * Opens Stripe's payment sheet (cards, Google Pay) for a checkout session, then waits until the SERVER says it's paid
 * (the webhook) – the app never decides that itself. A closed sheet or a failure releases the reservation at once.
 */
export function usePaymentSheet() {
  const { initPaymentSheet, presentPaymentSheet } = useStripe();

  return useCallback(async (session: CheckoutSession, country = 'US'): Promise<SheetResult> => {
    const init = await initPaymentSheet({
      merchantDisplayName: session.merchantDisplayName,
      customerId: session.customerId,
      customerEphemeralKeySecret: session.ephemeralKey,
      paymentIntentClientSecret: session.clientSecret,
      googlePay: { merchantCountryCode: 'US', currencyCode: session.currency, testEnv: true },
      returnURL: 'shopcarparts://stripe-redirect',
      allowsDelayedPaymentMethods: false,
      defaultBillingDetails: { address: { country } },
    });
    if (init.error) {
      await PaymentsApi.cancelPayment(session.paymentId).catch(() => undefined);
      return { status: 'failed', message: init.error.message };
    }
    const { error } = await presentPaymentSheet();
    if (error) {
      // Closed or failed: give the reserved quantity back now (not after 15 minutes).
      await PaymentsApi.cancelPayment(session.paymentId).catch(() => undefined);
      return error.code === 'Canceled' ? { status: 'canceled' } : { status: 'failed', message: error.message };
    }
    return waitUntilPaid(session.paymentId);
  }, [initPaymentSheet, presentPaymentSheet]);
}

/** Polls the server until the webhook marked the payment (about a second normally). */
export async function waitUntilPaid(paymentId: string, timeoutMs = 30000, everyMs = 1500): Promise<SheetResult> {
  const until = Date.now() + timeoutMs;
  while (Date.now() < until) {
    try {
      const s = await PaymentsApi.paymentStatus(paymentId);
      if (s.status === 'Succeeded' && s.orderId) return { status: 'paid', orderId: s.orderId };
      if (s.status === 'Refunded') return { status: 'failed', message: 'Sorry – it sold out meanwhile. Your payment was refunded.' };
    } catch {
      // keep trying
    }
    await new Promise(resolve => setTimeout(resolve, everyMs));
  }
  return { status: 'processing' };
}
