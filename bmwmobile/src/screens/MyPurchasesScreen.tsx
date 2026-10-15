import React, { useState, useCallback } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, Alert, RefreshControl } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { CompositeScreenProps, useFocusEffect } from '@react-navigation/native';
import { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { MainTabParamList, RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { ApiService, ServerError } from '../services/api';
import { MarketApi } from '../services/marketApi';
import { PartRequest } from '../types/models';
import type { Order } from '../types/listing';
import { Badge } from '../components/Badge';
import { useMoney } from '../location/useMoney';
import { OrderTrackingCard } from '../components/OrderTrackingCard';
import { useLiveEvent } from '../services/notifications';
import { useAuth } from '../auth/AuthContext';
import { isSeller } from '../utils/roles';

type Props = CompositeScreenProps<
  BottomTabScreenProps<MainTabParamList, 'MyPurchases'>,
  NativeStackScreenProps<RootStackParamList>
>;

const ORDER_BADGE: Record<string, { label: string; variant: 'success' | 'primary' | 'dark' }> = {
  Open: { label: 'OPEN', variant: 'primary' },
  Matched: { label: 'MATCHED', variant: 'success' },
  Closed: { label: 'CLOSED', variant: 'dark' },
};

type Side = 'purchases' | 'sales';

/**
 * Orders with their tracking (no online payment in this phase) and the user's custom orders.
 * Buyers see their purchases. Sellers also get Sales (orders to fulfil: ready / handed over / shipped) and My listings.
 */
export const MyPurchasesScreen: React.FC<Props> = ({ navigation }) => {
  const money = useMoney();
  const seller = isSeller(useAuth().user);
  const [side, setSide] = useState<Side>('purchases');
  const [orders, setOrders] = useState<Order[]>([]);
  const [requests, setRequests] = useState<PartRequest[]>([]);
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    const [o, r] = await Promise.all([MarketApi.orders().catch(() => [] as Order[]), ApiService.getPartRequests()]);
    setOrders(o);
    setRequests(r);
    setLoading(false);
  }, []);

  // Reload each time the tab is opened (a sale or a custom-order match may have happened meanwhile).
  useFocusEffect(useCallback(() => { load(); }, [load]));

  // The other party moved an order on (dispatched, delivered …): show it at once.
  useLiveEvent('Notify', (n: { type?: string }) => {
    if (n?.type === 'orderUpdate') load();
  });

  const replaceOrder = (updated: Order) =>
    setOrders(list => list.map(o => (o.id === updated.id ? { ...updated, imageUrl: updated.imageUrl ?? o.imageUrl } : o)));

  const closeOrder = (order: PartRequest) =>
    Alert.alert('Close custom order?', "You won't be notified about this part any more.", [
      { text: 'Keep', style: 'cancel' },
      {
        text: 'Close order',
        style: 'destructive',
        onPress: async () => {
          try {
            const closed = await ApiService.closePartRequest(order.id);
            setRequests(list => list.map(o => (o.id === closed.id ? closed : o)));
          } catch (err) {
            Alert.alert('Not closed', err instanceof ServerError ? err.message : 'Please try again.');
          }
        },
      },
    ]);

  const showing: Side = seller ? side : 'purchases';
  const shown = orders.filter(o => (showing === 'sales' ? o.role === 'seller' : o.role !== 'seller'));
  const salesToDo = orders.filter(o => o.role === 'seller' && o.status !== 'Completed').length;

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.title}>Orders</Text>
        {seller ? (
          <TouchableOpacity onPress={() => navigation.navigate('MyListings')}>
            <Text style={styles.link}>My listings →</Text>
          </TouchableOpacity>
        ) : null}
      </View>

      {seller ? (
        <View style={styles.segment}>
          {(['purchases', 'sales'] as Side[]).map(s => (
            <TouchableOpacity
              key={s}
              style={[styles.segmentItem, showing === s && styles.segmentActive]}
              onPress={() => setSide(s)}
              activeOpacity={0.8}
              accessibilityRole="tab"
              accessibilityState={{ selected: showing === s }}
            >
              <Text style={[styles.segmentText, showing === s && styles.segmentTextActive]}>
                {s === 'purchases' ? 'Purchases' : `Sales${salesToDo ? ` (${salesToDo})` : ''}`}
              </Text>
            </TouchableOpacity>
          ))}
        </View>
      ) : null}

      <ScrollView
        contentContainerStyle={styles.scroll}
        refreshControl={<RefreshControl refreshing={loading} onRefresh={load} tintColor={COLORS.primaryGlow} />}
      >
        <Text style={styles.sectionTitle}>{showing === 'sales' ? 'SALES TO FULFIL' : 'MY PURCHASES'}</Text>
        {shown.length === 0 && !loading ? (
          <View style={styles.emptyCard}>
            <Text style={styles.emptyText}>
              {showing === 'sales'
                ? 'When someone buys your item or wins your auction, the order shows up here.'
                : 'When you win an auction or buy something, it shows up here.'}
            </Text>
          </View>
        ) : null}
        {shown.map(order => (
          <OrderTrackingCard
            key={order.id}
            order={order}
            money={money}
            onChange={replaceOrder}
            onOpenListing={() => navigation.navigate('PartDetails', { auctionId: order.auctionId })}
            onReview={o => navigation.navigate('WriteReview', {
              orderId: o.id, reviewId: o.reviewId, title: o.title, imageUrl: o.imageUrl, seller: o.seller,
            })}
            onMessage={() => navigation.navigate('Conversation', {
              listingId: order.auctionId,
              buyer: order.role === 'seller' ? order.buyer : undefined,
            })}
          />
        ))}

        {/* Custom orders (things the user wants to buy) */}
        {showing === 'purchases' ? <Text style={styles.sectionTitle}>MY CUSTOM ORDERS</Text> : null}
        {showing === 'purchases' && requests.length === 0 && !loading ? (
          <View style={styles.emptyCard}>
            <Text style={styles.emptyText}>No custom orders yet. When a search finds nothing, create one and we'll tell you when the part is listed.</Text>
          </View>
        ) : null}
        {(showing === 'purchases' ? requests : []).map(order => {
          const badge = ORDER_BADGE[order.status] ?? { label: order.status.toUpperCase(), variant: 'dark' as const };
          const vehicle = [order.year || null, order.make, order.model].filter(Boolean).join(' ');
          return (
            <View key={order.id} style={styles.card}>
              <View style={styles.orderTop}>
                <Text style={[styles.itemTitle, styles.orderTitle]} numberOfLines={2}>
                  {order.partName || order.partNumber || order.searchText}
                </Text>
                <Badge label={badge.label} variant={badge.variant} size="sm" />
              </View>
              <Text style={styles.meta}>
                {vehicle || 'Any vehicle'}
                {order.partNumber ? ` · Part no. ${order.partNumber}` : ''}
              </Text>
              {order.status === 'Matched' && order.matchedAuctionId ? (
                <TouchableOpacity style={styles.actionBtn} onPress={() => navigation.navigate('PartDetails', { auctionId: order.matchedAuctionId! })}>
                  <Text style={styles.actionText}>View matching part →</Text>
                </TouchableOpacity>
              ) : null}
              {order.status === 'Open' ? (
                <TouchableOpacity onPress={() => closeOrder(order)}>
                  <Text style={styles.closeText}>Close order</Text>
                </TouchableOpacity>
              ) : null}
            </View>
          );
        })}
      </ScrollView>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  header: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', padding: SPACING.md, backgroundColor: COLORS.surface, borderBottomWidth: 1, borderBottomColor: COLORS.border },
  title: { color: COLORS.text, fontSize: 18, fontWeight: '800' },
  link: { color: COLORS.primaryGlow, fontSize: 13, fontWeight: '700' },
  scroll: { padding: SPACING.md, paddingBottom: SPACING.xxl },
  segment: { flexDirection: 'row', backgroundColor: COLORS.surface, borderRadius: RADIUS.md, borderWidth: 1, borderColor: COLORS.border, padding: 4, marginHorizontal: SPACING.md, marginTop: SPACING.sm },
  segmentItem: { flex: 1, paddingVertical: 8, borderRadius: RADIUS.sm, alignItems: 'center' },
  segmentActive: { backgroundColor: COLORS.primary },
  segmentText: { color: COLORS.textSecondary, fontSize: 13, fontWeight: '700' },
  segmentTextActive: { color: COLORS.text },
  sectionTitle: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1, marginTop: SPACING.sm, marginBottom: SPACING.sm },
  emptyCard: { backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, padding: SPACING.md, borderWidth: 1, borderColor: COLORS.border, marginBottom: SPACING.md },
  emptyText: { color: COLORS.textMuted, fontSize: 13, lineHeight: 18 },
  card: { backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, padding: SPACING.md, borderWidth: 1, borderColor: COLORS.border, marginBottom: SPACING.md, gap: 6 },
  itemTitle: { color: COLORS.text, fontSize: 14, fontWeight: '700' },
  meta: { color: COLORS.textSecondary, fontSize: 12 },
  actionBtn: { backgroundColor: COLORS.primary, paddingVertical: 10, borderRadius: RADIUS.md, alignItems: 'center', marginTop: 4 },
  actionText: { color: COLORS.text, fontWeight: '800', fontSize: 13 },
  orderTop: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start', gap: SPACING.sm },
  orderTitle: { flex: 1 },
  closeText: { color: COLORS.textMuted, fontSize: 12, fontWeight: '700', alignSelf: 'flex-end' },
});
