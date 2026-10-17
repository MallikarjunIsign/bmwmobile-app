import React, { useEffect, useState } from 'react';
import { View, Text, StyleSheet, TouchableOpacity, Image, Alert, Linking, Modal, TextInput } from 'react-native';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Badge } from './Badge';
import { StatusTimeline } from './StatusTimeline';
import { MarketApi } from '../services/marketApi';
import { ServerError } from '../services/api';
import { ShippingAddressSheet, addressText } from './ShippingAddressSheet';
import type { Address, DeliveryOption, Order, OrderStatus } from '../types/listing';
import { formatWhen } from '../utils/time';
import { orderShippingText } from '../utils/delivery';
import { formatDistance, formatRadius, haversineKm } from '../utils/geo';
import { useLocations } from '../location/LocationContext';
import { PaymentsApi, ONLINE_PAYMENT_COUNTRIES, centsText, timeLeftText } from '../services/paymentsApi';
import { usePaymentSheet } from '../payments/PaymentsProvider';
import { useNow } from './Countdown';

export const methodLabel = (method?: string | null) =>
  method === 'Pickup' ? 'Pickup' : method === 'Shipping' ? 'Shipping' : 'Not chosen yet';

/** Why the buyer reports a problem (they can add details). */
export const PROBLEM_REASONS = ['Not received', 'Not as described', 'Damaged', 'Other'];

const dayText = (iso: string) => new Date(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'short' });

interface Props {
  order: Order;
  money: (amount: number, decimals?: number) => string;
  onChange: (order: Order) => void;
  onOpenListing: () => void;
  /** "Message seller" / "Message buyer": the chat about this item. */
  onMessage?: () => void;
  /** "How was your purchase?": write / view the review of this order. */
  onReview?: (order: Order) => void;
}

/**
 * "Part price ₹14,000 + winning bid ₹100 + shipping ₹350 (22 km) = ₹14,450" or "… + pickup free = ₹14,100".
 * The shipping charge is the one the backend fixed on the order; nothing about delivery while it isn't chosen.
 */
export function totalBreakdown(order: Order, money: (n: number) => string): string {
  const qty = order.quantity ?? 1;
  const partPrice = order.partPrice ?? order.finalTotal;
  const parts = [order.saleType === 'FixedPrice' && qty > 1 ? `${qty} × ${money(partPrice / qty)}` : `Part price ${money(partPrice)}`];
  if (order.winningBid) parts.push(`winning bid ${money(order.winningBid)}`);
  const delivery = orderShippingText(order, money);
  if (delivery) parts.push(delivery);
  return parts.length > 1 ? `${parts.join(' + ')} = ${money(order.total ?? order.finalTotal)}` : parts[0];
}

/**
 * One sale / purchase with its tracking: a step bar (Sold → Shipped → Delivered → Completed, or Sold → Ready for
 * pickup → Picked up → Completed), the full timeline with dates on "Track order", the total, and the next action –
 * the buyer chooses Pickup or Shipping (with the address), the seller moves it forward, the buyer confirms.
 */
export const OrderTrackingCard: React.FC<Props> = ({ order, money, onChange, onOpenListing, onMessage, onReview }) => {
  const [expanded, setExpanded] = useState(false);
  const [busy, setBusy] = useState(false);
  const [method, setMethod] = useState<DeliveryOption | null>(order.deliveryMethod ?? null);
  const { location, country } = useLocations();
  const choosing = order.deliveryChoices.length > 1;
  // How far the item is from the buyer's location: outside their radius, Shipping is pre-selected (Pickup stays
  // possible – the buyer may want to travel).
  const [awayKm, setAwayKm] = useState<number | null>(null);
  useEffect(() => {
    if (!choosing || !location) return;
    let alive = true;
    MarketApi.listingDetail(order.auctionId)
      .then(l => {
        if (!alive || l.latitude == null || l.longitude == null) return;
        const km = haversineKm(location.latitude, location.longitude, l.latitude, l.longitude);
        setAwayKm(km);
        if (km > location.radiusKm && order.deliveryChoices.includes('Shipping')) setMethod(m => m ?? 'Shipping');
      })
      .catch(() => undefined);
    return () => {
      alive = false;
    };
  }, [choosing, order.auctionId, location?.latitude, location?.longitude, location?.radiusKm]); // eslint-disable-line react-hooks/exhaustive-deps
  const pickupFar = awayKm != null && location != null && awayKm > location.radiusKm;
  const paySheet = usePaymentSheet();
  const now = useNow(order.status === 'AwaitingPayment');
  const [addressSheet, setAddressSheet] = useState(false);
  const [dispatch, setDispatch] = useState<{ status: OrderStatus; label: string } | null>(null);
  const [courier, setCourier] = useState('');
  const [trackingNo, setTrackingNo] = useState('');
  const [problem, setProblem] = useState(false);
  const [problemReason, setProblemReason] = useState(PROBLEM_REASONS[0]);
  const [problemText, setProblemText] = useState('');

  const seller = order.role === 'seller';
  const done = order.status === 'Completed';
  const action = order.actions[0];
  const nextUp = order.tracking.findIndex(s => !s.done);

  const run = async (work: () => Promise<Order>) => {
    setBusy(true);
    try {
      onChange(await work());
    } catch (e) {
      Alert.alert('Not updated', e instanceof ServerError ? e.message : 'Please try again.');
    } finally {
      setBusy(false);
    }
  };

  // The buyer's button: "Confirm delivery" (shipping) or "Confirm you picked it up" (pickup).
  const actionText = !action ? '' : seller ? `Mark as ${action.label.toLowerCase()}`
    : action.status === 'Delivered' ? 'Confirm delivery'
    : order.deliveryMethod === 'Pickup' ? 'Confirm you picked it up' : 'Confirm you received it';

  const doAction = () => {
    if (!action) return;
    // Shipped: optional courier name and tracking number.
    if (action.status === 'Shipped') {
      setCourier(order.courier ?? '');
      setTrackingNo(order.trackingNumber ?? '');
      setDispatch(action);
      return;
    }
    Alert.alert(
      seller ? `Mark as ${action.label.toLowerCase()}?` : `${actionText}?`,
      seller ? 'The buyer is notified.' : 'Only confirm once you have the item. This completes the order.',
      [
        { text: 'Not yet', style: 'cancel' },
        {
          text: seller ? action.label : 'Confirm',
          onPress: () =>
            run(async () => {
              const updated = await MarketApi.orderStep(order.id, action.status);
              // The buyer just completed it: ask for a review straight away.
              if (!seller && updated.status === 'Completed' && updated.canReview && onReview)
                Alert.alert('How was your purchase?', 'Rate the item and the seller – it helps other buyers.', [
                  { text: 'Later', style: 'cancel' },
                  { text: 'Rate now', onPress: () => onReview(updated) },
                ]);
              return updated;
            }),
        },
      ],
    );
  };

  const chooseDelivery = () => {
    if (!method) return;
    if (method === 'Shipping') setAddressSheet(true);
    else run(() => MarketApi.chooseDelivery(order.id, 'Pickup'));
  };

  // The sheet shows field errors itself, so errors are passed on to it rather than shown in an alert.
  const shipTo = async (to: { addressId?: string; address?: Address; latitude?: number; longitude?: number }) => {
    const updated = await MarketApi.chooseDelivery(order.id, 'Shipping', to);
    setAddressSheet(false);
    onChange(updated);
  };

  // ── Online payment (USA / Ireland) ──
  const awaitingPayment = order.status === 'AwaitingPayment';
  const needsDelivery = !order.deliveryMethod || !!order.needsShippingAddress;
  const onlineCountry = ONLINE_PAYMENT_COUNTRIES.includes((order.countryCode ?? '').toUpperCase());
  const payNow = async () => {
    setBusy(true);
    try {
      const session = await PaymentsApi.payOrder(order.id);
      const result = await paySheet(session, order.countryCode ?? 'US');
      if (result.status === 'paid' || result.status === 'processing') {
        Alert.alert(result.status === 'paid' ? 'Paid ✓' : 'Payment received',
          result.status === 'paid' ? 'The seller has been told to get it ready.' : 'Stripe is confirming it – the order updates in a moment.');
        onChange(await MarketApi.order(order.id));
      } else if (result.status === 'failed') {
        Alert.alert('Payment not completed', result.message);
      }
    } catch (e) {
      Alert.alert('Payment not started', e instanceof ServerError ? e.message : 'Please try again.');
    } finally {
      setBusy(false);
    }
  };

  // ── After paying (Step 3) ──
  const reportProblem = () => {
    setProblem(false);
    const text = [problemReason, problemText.trim()].filter(Boolean).join(': ');
    run(async () => {
      const updated = await PaymentsApi.reportProblem(order.id, text);
      Alert.alert('Problem reported', 'The order is on hold and the seller isn’t paid until it’s sorted out. Message the seller – support will step in if needed.');
      return updated;
    });
  };

  const cancelAndRefund = () =>
    Alert.alert('Cancel and refund?', `The buyer gets ${money(order.total ?? order.finalTotal)} back to their card and the order is cancelled. This can't be undone.`, [
      { text: 'Keep order', style: 'cancel' },
      { text: 'Cancel and refund', style: 'destructive', onPress: () => run(() => PaymentsApi.cancelAndRefund(order.id, 'The seller can’t fulfil the order')) },
    ]);

  return (
    <View style={styles.card}>
      <TouchableOpacity style={styles.cardTop} onPress={onOpenListing}>
        {order.imageUrl ? <Image source={{ uri: order.imageUrl }} style={styles.thumb} /> : <View style={styles.thumb} />}
        <View style={styles.headerCol}>
          <View style={styles.badgeRow}>
            <Badge label={seller ? 'YOU SOLD' : 'YOU BOUGHT'} variant={seller ? 'primary' : 'success'} size="sm" />
            <Badge label={order.statusLabel.toUpperCase()} variant={done ? 'success' : 'dark'} size="sm" />
          </View>
          <Text style={styles.itemTitle} numberOfLines={2}>{order.title}</Text>
          <Text style={styles.total}>
            {(order.quantity ?? 1) > 1 ? `${order.quantity} units · ` : ''}Total: {money(order.total ?? order.finalTotal)}
          </Text>
        </View>
      </TouchableOpacity>
      <Text style={styles.metaSmall}>{totalBreakdown(order, money)}</Text>

      {/* Online payment: pay before the deadline (buyer) / waiting for it (seller). */}
      {awaitingPayment ? (
        <View style={styles.payBox} testID="order-payment">
          <Text style={styles.payTitle}>
            💳 {seller
              ? order.paymentLabel ?? 'Waiting for payment'
              : order.paymentDueAt ? `Pay within ${timeLeftText(order.paymentDueAt, now)}` : 'Waiting for your payment'}
          </Text>
          {!seller && order.canPay ? (
            needsDelivery ? (
              <Text style={styles.metaSmall}>Choose pickup or shipping below first – the shipping charge is part of the total.</Text>
            ) : (
              <TouchableOpacity style={[styles.payBtn, busy && styles.disabled]} onPress={payNow} disabled={busy} accessibilityRole="button">
                <Text style={styles.actionText}>PAY NOW – {money(order.total ?? order.finalTotal)}</Text>
              </TouchableOpacity>
            )
          ) : null}
          {!seller ? <Text style={styles.metaSmall}>Paid to ShopCarParts and held until you confirm you received it. No cash on delivery.</Text> : null}
        </View>
      ) : null}
      {order.status === 'Cancelled' ? (
        <Text style={styles.cancelled}>✕ {order.paymentLabel ?? order.cancelReason ?? 'Cancelled'}</Text>
      ) : null}
      {order.paymentStatus === 'Paid' ? <Text style={styles.paid}>✓ Paid online</Text> : null}
      {/* After paying: hold, refunds, the seller's payout ("about €…" for Irish sellers), auto-complete, the receipt. */}
      {order.onHold ? <Text style={styles.hold} testID="order-hold">⏸ On hold – {order.holdReason ?? 'being reviewed'}</Text> : null}
      {order.refundedCents ? (
        <Text style={styles.metaSmall}>↩ Refunded {centsText(order.refundedCents, money)} to {seller ? 'the buyer’s' : 'your'} card</Text>
      ) : null}
      {seller && order.payout ? <Text style={styles.payout} testID="order-payout">💰 {order.payout.label}</Text> : null}
      {!seller && order.paymentStatus === 'Paid' && !done && !order.onHold && order.autoCompleteAt ? (
        <Text style={styles.metaSmall}>
          Completes automatically on {dayText(order.autoCompleteAt)} and the seller is paid – report a problem before then if something is wrong.
        </Text>
      ) : null}
      {!seller && order.receiptUrl ? (
        <TouchableOpacity onPress={() => Linking.openURL(order.receiptUrl!)} hitSlop={8} accessibilityRole="link">
          <Text style={styles.link}>🧾 View receipt</Text>
        </TouchableOpacity>
      ) : null}
      {!seller && order.paymentStatus === 'NotRequired' && !onlineCountry ? (
        <Text style={styles.metaSmall}>Online payment is coming soon in your country – pay the seller directly.</Text>
      ) : null}

      {/* Step bar: Sold ─ Shipped ─ Delivered ─ Completed (or the pickup steps) */}
      <View style={styles.bar} accessibilityLabel={`Order status: ${order.statusLabel}`}>
        {order.tracking.map((s, i) => (
          <View key={s.status} style={styles.barStep}>
            <View style={styles.barTrack}>
              <View style={[styles.barLine, i === 0 && styles.hidden, s.done && styles.barLineDone]} />
              <View style={[styles.barDot, s.done && styles.barDotDone, i === nextUp && styles.barDotNext]}>
                {s.done ? <Text style={styles.barCheck}>✓</Text> : null}
              </View>
              <View style={[styles.barLine, i === order.tracking.length - 1 && styles.hidden, order.tracking[i + 1]?.done && styles.barLineDone]} />
            </View>
            <Text style={[styles.barText, s.done && styles.barTextDone, s.current && styles.barTextCurrent]} numberOfLines={2}>{s.label}</Text>
          </View>
        ))}
      </View>

      <TouchableOpacity onPress={() => setExpanded(x => !x)} hitSlop={8}>
        <Text style={styles.link}>{expanded ? 'Hide tracking ▴' : 'Track order ▾'}</Text>
      </TouchableOpacity>
      {expanded ? (
        <View style={styles.timeline}>
          <StatusTimeline
            steps={order.tracking.map((s, i) => ({
              title: s.label,
              subtitle: s.note ?? (s.done && !s.at && i > 0 ? 'Skipped' : undefined),
              date: formatWhen(s.at),
              completed: s.done,
              active: i === nextUp,
            }))}
          />
        </View>
      ) : null}

      <Text style={styles.meta}>
        {seller ? 'Buyer' : 'Seller'}: <Text style={styles.bold}>{order.otherParty}</Text>
        {order.locationName ? ` · 📍 ${order.locationName}` : ''}
      </Text>
      <Text style={styles.meta}>Delivery: <Text style={styles.bold}>{methodLabel(order.deliveryMethod)}</Text></Text>
      {order.deliveryMethod === 'Shipping' && (order.shipTo || order.deliveryAddress) ? (
        <Text style={styles.meta}>Ship to: {order.shipTo ? addressText(order.shipTo) : order.deliveryAddress}</Text>
      ) : null}
      {order.deliveryMethod === 'Pickup' && order.pickupAddress ? (
        <Text style={styles.meta}>Pickup at: <Text style={styles.bold}>{order.pickupAddress}</Text></Text>
      ) : null}
      {order.courier || order.trackingNumber ? (
        <Text style={styles.meta}>
          Courier: {[order.courier, order.trackingNumber ? `tracking ${order.trackingNumber}` : null].filter(Boolean).join(' · ')}
        </Text>
      ) : null}

      {/* Contact details are only shown to the two parties after the sale. */}
      <View style={styles.contactRow}>
        {onMessage ? (
          <TouchableOpacity style={styles.contactBtn} onPress={onMessage}>
            <Text style={styles.contactText}>💬 Message {seller ? 'buyer' : 'seller'}</Text>
          </TouchableOpacity>
        ) : null}
        {order.contactPhone ? (
          <TouchableOpacity style={styles.contactBtn} onPress={() => Linking.openURL(`tel:${order.contactPhone}`)}>
            <Text style={styles.contactText}>📞 Call</Text>
          </TouchableOpacity>
        ) : null}
        {order.contactEmail ? (
          <TouchableOpacity style={styles.contactBtn}
            onPress={() => Linking.openURL(`mailto:${order.contactEmail}?subject=${encodeURIComponent(`ShopCarParts: ${order.title}`)}`)}>
            <Text style={styles.contactText}>✉️ Email</Text>
          </TouchableOpacity>
        ) : null}
        {!order.contactPhone && !order.contactEmail ? (
          <Text style={styles.metaSmall}>The {seller ? 'buyer' : 'seller'}'s contact appears once they open their Orders.</Text>
        ) : null}
      </View>

      {/* Buyer: Pickup or Shipping (when both were offered) – or the address for a shipping-only listing. */}
      {/* (Waiting for payment with the delivery chosen: "Pay now" instead.) */}
      {order.deliveryChoices.length > 1 && !(awaitingPayment && !needsDelivery) ? (
        <View style={styles.choose}>
          <Text style={styles.chooseTitle}>How do you want to receive it?</Text>
          {order.deliveryChoices.map(c => (
            <TouchableOpacity key={c} style={[styles.option, method === c && styles.optionActive]} onPress={() => setMethod(c)}
              accessibilityState={{ selected: method === c }}>
              <Text style={styles.optionTitle}>{c === 'Pickup' ? '📍 I will pick it up · free' : '🚚 Ship it to me'}</Text>
              <Text style={styles.metaSmall}>
                {c === 'Pickup'
                  ? pickupFar
                    ? `In ${order.locationName ?? 'the seller’s area'} – ${formatDistance(awayKm!, country)} away, outside your ${formatRadius(location!.radiusKm, country)} radius`
                    : `In ${order.locationName ?? 'the seller’s area'} – the exact address is shown once you choose`
                  : 'Choose your delivery location next – you see the exact shipping charge before confirming'}
              </Text>
            </TouchableOpacity>
          ))}
          <TouchableOpacity style={[styles.actionBtn, (!method || busy) && styles.disabled]} onPress={chooseDelivery} disabled={!method || busy}>
            <Text style={styles.actionText}>{method === 'Shipping' ? 'Next: shipping address' : 'Confirm pickup'}</Text>
          </TouchableOpacity>
        </View>
      ) : order.needsShippingAddress && !seller ? (
        <View style={styles.choose}>
          <Text style={styles.chooseTitle}>🚚 This item is shipped</Text>
          <Text style={styles.metaSmall}>Add where it should go – you see the exact shipping charge before confirming.</Text>
          <TouchableOpacity style={[styles.actionBtn, busy && styles.disabled]} onPress={() => setAddressSheet(true)} disabled={busy}>
            <Text style={styles.actionText}>Add shipping address</Text>
          </TouchableOpacity>
        </View>
      ) : null}

      {/* After completion: "Rate your purchase" (buyer, 30 days) or the review they wrote. */}
      {!seller && onReview && (order.canReview || order.reviewId) ? (
        <TouchableOpacity style={[styles.actionBtn, !order.canReview && styles.secondaryBtn]} onPress={() => onReview(order)}>
          <Text style={styles.actionText}>{order.canReview ? '⭐ Rate your purchase' : '⭐ Your review'}</Text>
        </TouchableOpacity>
      ) : null}

      {action ? (
        <TouchableOpacity style={[styles.actionBtn, busy && styles.disabled]} onPress={doAction} disabled={busy}>
          <Text style={styles.actionText}>{actionText}</Text>
        </TouchableOpacity>
      ) : !done ? (
        <Text style={styles.waiting}>
          {seller
            ? !order.deliveryMethod
              ? 'Waiting for the buyer to choose pickup or shipping'
              : order.needsShippingAddress
                ? 'Waiting for the buyer’s shipping address'
                : `Waiting for the buyer to confirm (${order.deliveryMethod === 'Shipping' ? 'delivered' : 'picked up'})`
            : ''}
        </Text>
      ) : null}

      {!seller && order.canReportProblem ? (
        <TouchableOpacity style={[styles.actionBtn, styles.secondaryBtn, busy && styles.disabled]} onPress={() => setProblem(true)} disabled={busy}>
          <Text style={styles.actionText}>⚠️ Report a problem</Text>
        </TouchableOpacity>
      ) : null}
      {seller && order.canCancelAndRefund ? (
        <TouchableOpacity style={[styles.actionBtn, styles.secondaryBtn, busy && styles.disabled]} onPress={cancelAndRefund} disabled={busy}>
          <Text style={styles.actionText}>Cancel and refund</Text>
        </TouchableOpacity>
      ) : null}

      {/* Report a problem: the order goes on hold until it's sorted out. */}
      <Modal visible={problem} transparent animationType="fade" onRequestClose={() => setProblem(false)}>
        <View style={styles.modalBackdrop}>
          <View style={styles.modalCard}>
            <Text style={styles.chooseTitle}>What's wrong?</Text>
            <Text style={styles.metaSmall}>The order goes on hold: it won't complete by itself and the seller isn't paid until it's sorted out.</Text>
            {PROBLEM_REASONS.map(r => (
              <TouchableOpacity key={r} style={[styles.option, problemReason === r && styles.optionActive]} onPress={() => setProblemReason(r)}
                accessibilityState={{ selected: problemReason === r }}>
                <Text style={styles.optionTitle}>{r}</Text>
              </TouchableOpacity>
            ))}
            <TextInput style={styles.input} placeholder="Details (optional)" placeholderTextColor={COLORS.textMuted}
              value={problemText} onChangeText={setProblemText} maxLength={200} />
            <View style={styles.modalButtons}>
              <TouchableOpacity onPress={() => setProblem(false)}>
                <Text style={styles.cancel}>Cancel</Text>
              </TouchableOpacity>
              <TouchableOpacity style={styles.modalConfirm} onPress={reportProblem}>
                <Text style={styles.actionText}>Report problem</Text>
              </TouchableOpacity>
            </View>
          </View>
        </View>
      </Modal>

      {!seller ? (
        <ShippingAddressSheet visible={addressSheet} countryCode={order.countryCode ?? 'IN'} listingId={order.auctionId} money={money}
          onClose={() => setAddressSheet(false)} onSubmit={shipTo} />
      ) : null}

      {/* Shipped: optional courier and tracking number */}
      <Modal visible={!!dispatch} transparent animationType="fade" onRequestClose={() => setDispatch(null)}>
        <View style={styles.modalBackdrop}>
          <View style={styles.modalCard}>
            <Text style={styles.chooseTitle}>Mark as {dispatch?.label.toLowerCase()}</Text>
            <Text style={styles.metaSmall}>Add the courier details so the buyer can follow it (optional).</Text>
            <TextInput style={styles.input} placeholder="Courier (e.g. Delhivery, UPS, GIG Logistics)" placeholderTextColor={COLORS.textMuted}
              value={courier} onChangeText={setCourier} maxLength={60} />
            <TextInput style={styles.input} placeholder="Tracking / AWB number" placeholderTextColor={COLORS.textMuted}
              value={trackingNo} onChangeText={setTrackingNo} maxLength={60} autoCapitalize="characters" />
            <View style={styles.modalButtons}>
              <TouchableOpacity onPress={() => setDispatch(null)}>
                <Text style={styles.cancel}>Cancel</Text>
              </TouchableOpacity>
              <TouchableOpacity
                style={styles.modalConfirm}
                onPress={() => {
                  const step = dispatch!;
                  setDispatch(null);
                  run(() => MarketApi.orderStep(order.id, step.status, { courier: courier.trim() || undefined, trackingNumber: trackingNo.trim() || undefined }));
                }}
              >
                <Text style={styles.actionText}>{dispatch?.label}</Text>
              </TouchableOpacity>
            </View>
          </View>
        </View>
      </Modal>
    </View>
  );
};

const styles = StyleSheet.create({
  card: { backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, padding: SPACING.md, borderWidth: 1, borderColor: COLORS.border, marginBottom: SPACING.md, gap: 6 },
  cardTop: { flexDirection: 'row', gap: SPACING.md },
  thumb: { width: 72, height: 72, borderRadius: RADIUS.md, backgroundColor: COLORS.card },
  headerCol: { flex: 1, gap: 3, alignItems: 'flex-start' },
  badgeRow: { flexDirection: 'row', gap: 6, flexWrap: 'wrap' },
  itemTitle: { color: COLORS.text, fontSize: 14, fontWeight: '700' },
  total: { color: COLORS.primaryGlow, fontSize: 14, fontWeight: '800' },
  bar: { flexDirection: 'row', marginTop: SPACING.sm },
  barStep: { flex: 1, alignItems: 'center', gap: 4 },
  barTrack: { flexDirection: 'row', alignItems: 'center', alignSelf: 'stretch' },
  barLine: { flex: 1, height: 2, backgroundColor: COLORS.border },
  barLineDone: { backgroundColor: COLORS.successGlow },
  hidden: { opacity: 0 },
  barDot: { width: 18, height: 18, borderRadius: 9, backgroundColor: COLORS.card, borderWidth: 2, borderColor: COLORS.border, alignItems: 'center', justifyContent: 'center' },
  barDotDone: { backgroundColor: COLORS.successGlow, borderColor: COLORS.successGlow },
  barDotNext: { borderColor: COLORS.primaryGlow },
  barCheck: { color: COLORS.background, fontSize: 10, fontWeight: '900' },
  barText: { color: COLORS.textMuted, fontSize: 10, textAlign: 'center', paddingHorizontal: 2 },
  barTextDone: { color: COLORS.successGlow },
  barTextCurrent: { fontWeight: '800' },
  link: { color: COLORS.primaryGlow, fontSize: 12, fontWeight: '700' },
  timeline: { backgroundColor: COLORS.card, borderRadius: RADIUS.md, padding: SPACING.sm },
  meta: { color: COLORS.textSecondary, fontSize: 12 },
  metaSmall: { color: COLORS.textMuted, fontSize: 11 },
  bold: { color: COLORS.text, fontWeight: '800' },
  contactRow: { flexDirection: 'row', gap: SPACING.sm, flexWrap: 'wrap' },
  contactBtn: { backgroundColor: COLORS.cardSecondary, paddingHorizontal: SPACING.md, paddingVertical: 8, borderRadius: RADIUS.md },
  contactText: { color: COLORS.text, fontWeight: '700', fontSize: 12 },
  choose: { gap: 6, marginTop: 4 },
  chooseTitle: { color: COLORS.text, fontSize: 14, fontWeight: '800' },
  option: { borderWidth: 1, borderColor: COLORS.border, borderRadius: RADIUS.md, padding: SPACING.sm, gap: 2 },
  optionActive: { borderColor: COLORS.primaryGlow, backgroundColor: COLORS.card },
  optionTitle: { color: COLORS.text, fontSize: 13, fontWeight: '700' },
  actionBtn: { backgroundColor: COLORS.primary, paddingVertical: 10, borderRadius: RADIUS.md, alignItems: 'center', marginTop: 4 },
  actionText: { color: COLORS.text, fontWeight: '800', fontSize: 13 },
  payBox: { backgroundColor: COLORS.card, borderRadius: RADIUS.md, padding: SPACING.sm, borderWidth: 1, borderColor: COLORS.warningGlow, gap: 6, marginTop: SPACING.xs },
  payTitle: { color: COLORS.warningGlow, fontSize: 13, fontWeight: '800' },
  payBtn: { backgroundColor: COLORS.success, paddingVertical: 11, borderRadius: RADIUS.md, alignItems: 'center' },
  cancelled: { color: COLORS.danger, fontSize: 12, fontWeight: '700', marginTop: 4 },
  paid: { color: COLORS.successGlow, fontSize: 12, fontWeight: '700', marginTop: 4 },
  payout: { color: COLORS.text, fontSize: 12, fontWeight: '700' },
  hold: { color: COLORS.warningGlow, fontSize: 12, fontWeight: '700' },
  secondaryBtn: { backgroundColor: COLORS.cardSecondary },
  disabled: { opacity: 0.5 },
  waiting: { color: COLORS.textMuted, fontSize: 12, fontStyle: 'italic' },
  modalBackdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.6)', justifyContent: 'center', padding: SPACING.lg },
  modalCard: { backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, padding: SPACING.md, gap: SPACING.sm, borderWidth: 1, borderColor: COLORS.border },
  input: { backgroundColor: COLORS.card, color: COLORS.text, borderRadius: RADIUS.md, paddingHorizontal: SPACING.md, paddingVertical: 10, borderWidth: 1, borderColor: COLORS.border },
  modalButtons: { flexDirection: 'row', justifyContent: 'flex-end', alignItems: 'center', gap: SPACING.lg, marginTop: SPACING.sm },
  cancel: { color: COLORS.textMuted, fontWeight: '700' },
  modalConfirm: { backgroundColor: COLORS.primary, paddingVertical: 10, paddingHorizontal: SPACING.lg, borderRadius: RADIUS.md },
});
