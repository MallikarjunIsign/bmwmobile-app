import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, TouchableOpacity, Alert } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { PaymentsApi, timeLeftText, type SecondChanceOffer } from '../services/paymentsApi';
import { ServerError } from '../services/api';
import { useNow } from './Countdown';

/**
 * Orders → "Second chance" (buyers): the winner didn't pay, so the item is offered to you at your own highest bid.
 * Accept → an order waiting for your payment (same deadline); Decline → it goes to the next bidder. No penalty either way.
 */
export const SecondChanceOffers: React.FC<{ money: (n: number) => string; onAccepted: () => void }> = ({ money, onAccepted }) => {
  const [offers, setOffers] = useState<SecondChanceOffer[]>([]);
  const [busy, setBusy] = useState<string | null>(null);
  const now = useNow();

  const load = useCallback(() => {
    PaymentsApi.offers().then(setOffers).catch(() => setOffers([]));
  }, []);
  useFocusEffect(load);

  const answer = async (offer: SecondChanceOffer, accept: boolean) => {
    setBusy(offer.id);
    try {
      if (accept) await PaymentsApi.acceptOffer(offer.id);
      else await PaymentsApi.declineOffer(offer.id);
      setOffers(list => list.filter(o => o.id !== offer.id));
      if (accept) {
        onAccepted();
        Alert.alert('Accepted', 'It is in your purchases now – choose pickup or shipping and pay before the time runs out.');
      }
    } catch (e) {
      Alert.alert('Not saved', e instanceof ServerError ? e.message : 'Please try again.');
      load();
    } finally {
      setBusy(null);
    }
  };

  if (offers.length === 0) return null;
  return (
    <View testID="second-chance-offers">
      <Text style={styles.section}>SECOND CHANCE OFFERS</Text>
      {offers.map(o => (
        <View key={o.id} style={styles.card}>
          <Text style={styles.title} numberOfLines={2}>{o.title}</Text>
          <Text style={styles.text}>
            The winner didn't pay. It's yours for your highest bid of <Text style={styles.bold}>{money(o.amount)}</Text>.
          </Text>
          <Text style={styles.time}>⏱ Answer and pay within {timeLeftText(o.expiresAt, now)}</Text>
          <View style={styles.row}>
            <TouchableOpacity style={[styles.accept, busy !== null && styles.disabled]} onPress={() => answer(o, true)} disabled={busy !== null}>
              <Text style={styles.btnText}>ACCEPT & PAY</Text>
            </TouchableOpacity>
            <TouchableOpacity style={[styles.decline, busy !== null && styles.disabled]} onPress={() => answer(o, false)} disabled={busy !== null}>
              <Text style={styles.declineText}>Decline</Text>
            </TouchableOpacity>
          </View>
        </View>
      ))}
    </View>
  );
};

const styles = StyleSheet.create({
  section: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1, marginTop: SPACING.sm, marginBottom: SPACING.sm },
  card: { backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, padding: SPACING.md, borderWidth: 1, borderColor: COLORS.warningGlow, marginBottom: SPACING.md, gap: 6 },
  title: { color: COLORS.text, fontSize: 14, fontWeight: '700' },
  text: { color: COLORS.textSecondary, fontSize: 13, lineHeight: 18 },
  bold: { color: COLORS.text, fontWeight: '800' },
  time: { color: COLORS.warningGlow, fontSize: 12, fontWeight: '700' },
  row: { flexDirection: 'row', gap: SPACING.sm, marginTop: 4 },
  accept: { flex: 2, backgroundColor: COLORS.success, paddingVertical: 10, borderRadius: RADIUS.md, alignItems: 'center' },
  decline: { flex: 1, borderWidth: 1, borderColor: COLORS.border, paddingVertical: 10, borderRadius: RADIUS.md, alignItems: 'center' },
  btnText: { color: COLORS.text, fontWeight: '800', fontSize: 13 },
  declineText: { color: COLORS.textSecondary, fontWeight: '700', fontSize: 13 },
  disabled: { opacity: 0.5 },
});
