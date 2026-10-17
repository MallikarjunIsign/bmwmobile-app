import React, { useCallback, useEffect, useState } from 'react';
import { View, Text, StyleSheet, TouchableOpacity, ActivityIndicator, AppState, Linking } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { PaymentsApi, sellerPaymentView, type SellerPaymentAccount } from '../services/paymentsApi';
import { ServerError } from '../services/api';

const TONES = { success: COLORS.successGlow, warning: COLORS.warningGlow, danger: COLORS.danger, muted: COLORS.textMuted } as const;

/**
 * Profile → Payments (sellers): the Stripe payment account's status – Not started / Pending verification / Active /
 * Action needed – with the button to Stripe's hosted setup (opened in the browser, comes back to the app), and the
 * Stripe dashboard for payouts. In India / Nigeria: "Online payment coming soon in your country".
 */
export const SellerPaymentsCard: React.FC = () => {
  const [account, setAccount] = useState<SellerPaymentAccount | null>(null);
  const [busy, setBusy] = useState<'load' | 'setup' | 'dashboard' | null>('load');
  const [error, setError] = useState<string | null>(null);

  // First visit opens the account (USA / Ireland); afterwards the status is read again from Stripe.
  const load = useCallback(async (create: boolean) => {
    try {
      setAccount(await (create ? PaymentsApi.ensureSellerAccount() : PaymentsApi.sellerAccount()));
      setError(null);
    } catch (e) {
      setError(e instanceof ServerError ? e.message : "Couldn't load your payment setup.");
    } finally {
      setBusy(b => (b === 'load' ? null : b));
    }
  }, []);

  useFocusEffect(
    useCallback(() => {
      load(true);
    }, [load]),
  );
  // Back from Stripe's page in the browser: show the new status.
  useEffect(() => {
    const sub = AppState.addEventListener('change', state => state === 'active' && load(false));
    return () => sub.remove();
  }, [load]);

  const open = async (which: 'setup' | 'dashboard') => {
    setBusy(which);
    setError(null);
    try {
      await Linking.openURL(which === 'setup' ? await PaymentsApi.onboardingLink() : await PaymentsApi.dashboardLink());
    } catch (e) {
      setError(e instanceof ServerError ? e.message : "Couldn't open Stripe. Please try again.");
    } finally {
      setBusy(null);
    }
  };

  const view = sellerPaymentView(account);
  return (
    <View style={styles.card} testID="seller-payments">
      <View style={styles.head}>
        <Text style={styles.title}>💳 Payments</Text>
        {busy === 'load' && !account ? <ActivityIndicator color={COLORS.primaryGlow} /> : (
          <Text style={[styles.status, { color: TONES[view.tone] }]}>{view.label}</Text>
        )}
      </View>
      {account ? <Text style={styles.text}>{account.message}</Text> : null}
      {account && account.status !== 'NotAvailable' && !account.canPublish ? (
        <Text style={styles.note}>You can publish new listings once payments are active.</Text>
      ) : null}
      {error ? <Text style={styles.error}>{error}</Text> : null}

      {view.action ? (
        <TouchableOpacity style={styles.button} onPress={() => open('setup')} disabled={busy !== null} accessibilityRole="button">
          {busy === 'setup' ? <ActivityIndicator color={COLORS.text} /> : <Text style={styles.buttonText}>{view.action}</Text>}
        </TouchableOpacity>
      ) : null}
      {account?.status === 'Active' || account?.status === 'Pending' ? (
        <TouchableOpacity style={styles.link} onPress={() => open('dashboard')} disabled={busy !== null} accessibilityRole="link">
          {busy === 'dashboard' ? <ActivityIndicator color={COLORS.primaryGlow} /> : <Text style={styles.linkText}>Payouts & bank account (Stripe) →</Text>}
        </TouchableOpacity>
      ) : null}
    </View>
  );
};

const styles = StyleSheet.create({
  card: { backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, padding: SPACING.md, borderWidth: 1, borderColor: COLORS.border, gap: 6 },
  head: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  title: { color: COLORS.text, fontSize: 15, fontWeight: '800' },
  status: { fontSize: 13, fontWeight: '800' },
  text: { color: COLORS.textSecondary, fontSize: 13, lineHeight: 18 },
  note: { color: COLORS.warningGlow, fontSize: 12 },
  error: { color: COLORS.danger, fontSize: 12 },
  button: { backgroundColor: COLORS.primary, paddingVertical: 12, borderRadius: RADIUS.md, alignItems: 'center', marginTop: SPACING.xs },
  buttonText: { color: COLORS.text, fontSize: 13, fontWeight: '900', letterSpacing: 0.5 },
  link: { paddingVertical: SPACING.xs },
  linkText: { color: COLORS.primaryGlow, fontSize: 13, fontWeight: '700' },
});
