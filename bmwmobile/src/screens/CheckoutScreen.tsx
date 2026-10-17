import React, { useEffect, useRef, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, ActivityIndicator, Alert, Image } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { ShippingAddressSheet } from '../components/ShippingAddressSheet';
import { useNow } from '../components/Countdown';
import { useMoney } from '../location/useMoney';
import { listingErrorMessage } from '../services/api';
import { PaymentsApi, checkoutTotalText, centsText, timeLeftText, type CheckoutSession, type DeliveryRequest } from '../services/paymentsApi';
import { usePaymentSheet } from '../payments/PaymentsProvider';

type Props = NativeStackScreenProps<RootStackParamList, 'Checkout'>;

/**
 * Buy now with online payment (USA / Ireland):
 *  1. Pickup or Shipping (with the address – the shipping charge is calculated by the server),
 *  2. the total from the server ("Item $120 + Shipping $12 = Total $132"); the quantity is reserved for 15 minutes,
 *  3. Stripe's payment sheet (cards, Google Pay). The order exists once Stripe's confirmation reaches the server.
 * Closing the sheet or leaving releases the reservation at once.
 */
export const CheckoutScreen: React.FC<Props> = ({ route, navigation }) => {
  const p = route.params;
  const money = useMoney();
  const now = useNow();
  const pay = usePaymentSheet();
  const [session, setSession] = useState<CheckoutSession | null>(null);
  const [method, setMethod] = useState<'Pickup' | 'Shipping' | null>(null);
  const [addressSheet, setAddressSheet] = useState(false);
  const [busy, setBusy] = useState<'start' | 'pay' | null>(null);
  const [note, setNote] = useState<string | null>(null);
  const open = useRef<CheckoutSession | null>(null);
  open.current = session;
  // A second tap before the first answer would start (and replace) a second checkout: one at a time.
  const starting = useRef(false);

  // Leaving without paying gives the reserved quantity back now.
  useEffect(() => () => {
    if (open.current) PaymentsApi.cancelPayment(open.current.paymentId).catch(() => undefined);
  }, []);

  const reservedLeft = session?.reservedUntil ? new Date(session.reservedUntil).getTime() - now : null;
  useEffect(() => {
    if (session && reservedLeft !== null && reservedLeft <= 0) {
      setSession(null);
      setNote('The 15-minute reservation ended. Start again to buy it.');
    }
  }, [session, reservedLeft]);

  const start = async (delivery: DeliveryRequest) => {
    if (starting.current) return;
    starting.current = true;
    setBusy('start');
    setNote(null);
    try {
      setSession(await PaymentsApi.buyNow(p.listingId, p.quantity, delivery));
      setMethod(delivery.method);
    } catch (e) {
      // The server's own reason when it answered ("Sorry, this is sold out", "no longer available" …).
      setNote(listingErrorMessage(e, "Couldn't start the payment. Please try again."));
      throw e;
    } finally {
      starting.current = false;
      setBusy(null);
    }
  };

  const choose = (m: 'Pickup' | 'Shipping') => {
    if (m === 'Shipping') setAddressSheet(true);
    else start({ method: 'Pickup' }).catch(() => undefined);
  };

  const payNow = async () => {
    if (!session || starting.current) return;
    starting.current = true;
    setBusy('pay');
    const result = await pay(session, p.countryCode).finally(() => { starting.current = false; });
    setBusy(null);
    if (result.status === 'paid' || result.status === 'processing') {
      open.current = null;
      setSession(null);
      Alert.alert(
        result.status === 'paid' ? 'Paid ✓' : 'Payment received',
        result.status === 'paid'
          ? 'Your order is in Orders. The seller has been told to get it ready.'
          : "Stripe is confirming it – your order appears in Orders in a moment.",
        [{ text: 'View order', onPress: () => navigation.navigate('MainTabs', { screen: 'MyPurchases' }) }],
      );
      return;
    }
    // Closed or failed: the reservation was released (the sheet's cancel).
    open.current = null;
    setSession(null);
    setNote(result.status === 'canceled' ? 'Payment cancelled – the item is no longer reserved for you.' : result.message);
  };

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Checkout" onBack={() => navigation.goBack()} />
      <ScrollView contentContainerStyle={styles.scroll}>
        <View style={styles.card}>
          {p.imageUrl ? <Image source={{ uri: p.imageUrl }} style={styles.thumb} /> : null}
          <View style={styles.flex}>
            <Text style={styles.title} numberOfLines={2}>{p.title}</Text>
            <Text style={styles.meta}>{p.quantity > 1 ? `${p.quantity} × ` : ''}{money(p.price)}</Text>
          </View>
        </View>

        {!session ? (
          <>
            <Text style={styles.section}>HOW DO YOU WANT TO RECEIVE IT?</Text>
            {p.deliveryOptions.map(o => (
              <TouchableOpacity key={o} style={[styles.option, method === o && styles.optionActive]} onPress={() => choose(o)}
                disabled={busy !== null} accessibilityRole="button">
                <Text style={styles.optionTitle}>{o === 'Pickup' ? '📍 I will pick it up · free' : '🚚 Ship it to me'}</Text>
                <Text style={styles.meta}>
                  {o === 'Pickup'
                    ? `In ${p.pickupCity ?? 'the seller’s area'} – the exact address is shown once you've paid`
                    : 'Choose the address next – you see the exact shipping charge first'}
                </Text>
              </TouchableOpacity>
            ))}
            {busy === 'start' ? <ActivityIndicator color={COLORS.primaryGlow} style={styles.spinner} /> : null}
          </>
        ) : (
          <>
            <Text style={styles.section}>TOTAL</Text>
            <View style={styles.card}>
              <View style={styles.flex}>
                <Text style={styles.breakdown} testID="checkout-breakdown">{checkoutTotalText(session, money)}</Text>
                <Text style={styles.meta}>{method === 'Shipping' ? '🚚 Shipping' : '📍 Pickup'} · paid to ShopCarParts, held until you confirm you received it</Text>
              </View>
            </View>
            {reservedLeft !== null ? (
              <Text style={styles.reserved} testID="checkout-reserved">⏱ Reserved for you: {timeLeftText(session.reservedUntil!, now)}</Text>
            ) : null}
            <TouchableOpacity style={[styles.payBtn, busy !== null && styles.disabled]} onPress={payNow} disabled={busy !== null} accessibilityRole="button">
              {busy === 'pay' ? <ActivityIndicator color={COLORS.text} /> : <Text style={styles.payText}>PAY {centsText(session.totalAmount, money)}</Text>}
            </TouchableOpacity>
            <TouchableOpacity
              onPress={() => {
                PaymentsApi.cancelPayment(session.paymentId).catch(() => undefined);
                open.current = null;
                setSession(null);
              }}
              disabled={busy !== null}
            >
              <Text style={styles.change}>Change delivery</Text>
            </TouchableOpacity>
          </>
        )}

        {note ? <Text style={styles.note}>{note}</Text> : null}
        <Text style={styles.small}>Card details go straight to Stripe – ShopCarParts never sees them. Test mode: no real money.</Text>
      </ScrollView>

      <ShippingAddressSheet
        visible={addressSheet}
        countryCode={p.countryCode}
        listingId={p.listingId}
        money={money}
        onClose={() => setAddressSheet(false)}
        onSubmit={async to => {
          await start({ method: 'Shipping', ...to });
          setAddressSheet(false);
        }}
      />
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  scroll: { padding: SPACING.md, paddingBottom: SPACING.xxl, gap: SPACING.sm },
  card: { flexDirection: 'row', gap: SPACING.md, backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, padding: SPACING.md, borderWidth: 1, borderColor: COLORS.border },
  thumb: { width: 64, height: 64, borderRadius: RADIUS.md, backgroundColor: COLORS.card },
  flex: { flex: 1, gap: 4 },
  title: { color: COLORS.text, fontSize: 15, fontWeight: '800' },
  meta: { color: COLORS.textSecondary, fontSize: 12, lineHeight: 17 },
  section: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1, marginTop: SPACING.md },
  option: { backgroundColor: COLORS.surface, borderRadius: RADIUS.md, padding: SPACING.md, borderWidth: 1, borderColor: COLORS.border, gap: 4 },
  optionActive: { borderColor: COLORS.primaryGlow },
  optionTitle: { color: COLORS.text, fontSize: 14, fontWeight: '700' },
  spinner: { marginTop: SPACING.md },
  breakdown: { color: COLORS.text, fontSize: 15, fontWeight: '800' },
  reserved: { color: COLORS.warningGlow, fontSize: 13, fontWeight: '700' },
  payBtn: { backgroundColor: COLORS.success, paddingVertical: 15, borderRadius: RADIUS.md, alignItems: 'center', marginTop: SPACING.sm },
  payText: { color: COLORS.text, fontSize: 15, fontWeight: '900', letterSpacing: 0.5 },
  disabled: { opacity: 0.6 },
  change: { color: COLORS.primaryGlow, fontSize: 13, fontWeight: '700', textAlign: 'center', marginTop: SPACING.sm },
  note: { color: COLORS.warningGlow, fontSize: 13, marginTop: SPACING.sm },
  small: { color: COLORS.textMuted, fontSize: 11, marginTop: SPACING.lg, textAlign: 'center' },
});
