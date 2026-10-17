import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, FlatList, TouchableOpacity, Image, Alert, RefreshControl } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect } from '@react-navigation/native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { Badge } from '../components/Badge';
import { MarketApi } from '../services/marketApi';
import { ServerError } from '../services/api';
import { useMoney } from '../location/useMoney';
import { label } from '../utils/time';
import { Countdown } from '../components/Countdown';
import type { MyListing } from '../types/listing';

type Props = NativeStackScreenProps<RootStackParamList, 'MyListings'>;

const FILTERS = ['All', 'Draft', 'Live', 'AwaitingPayment', 'EndedNoBids', 'Sold', 'Completed', 'Cancelled'] as const;
const VARIANT: Record<MyListing['state'], 'primary' | 'success' | 'warning' | 'dark' | 'info'> = {
  Draft: 'info', Live: 'primary', EndedNoBids: 'warning', Sold: 'success', Completed: 'success', Cancelled: 'dark',
  // Online payment (USA / Ireland): the winner pays first; nobody paid → relist.
  AwaitingPayment: 'warning', OfferedToNextBidder: 'warning', UnsoldNotPaid: 'warning',
};
/** Filter pills: "Awaiting payment" also shows listings offered to the next bidder; "Ended – no bids" also unsold-not-paid. */
const inFilter = (state: MyListing['state'], f: (typeof FILTERS)[number]) =>
  f === 'All' || state === f ||
  (f === 'AwaitingPayment' && state === 'OfferedToNextBidder') || (f === 'EndedNoBids' && state === 'UnsoldNotPaid');

/** My Listings: Draft / Live / Ended – no bids / Sold / Completed / Cancelled. */
export const MyListingsScreen: React.FC<Props> = ({ navigation }) => {
  const money = useMoney();
  const [items, setItems] = useState<MyListing[]>([]);
  const [filter, setFilter] = useState<(typeof FILTERS)[number]>('All');
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setItems(await MarketApi.myListings());
    } catch (e) {
      Alert.alert('Could not load', e instanceof ServerError ? e.message : 'Please try again.');
    } finally {
      setLoading(false);
    }
  }, []);

  useFocusEffect(useCallback(() => { load(); }, [load]));

  const relist = async (item: MyListing) => {
    try {
      const copy = await MarketApi.relist(item.id);
      Alert.alert('Relisted', 'Your listing is live again.');
      navigation.navigate('PartDetails', { auctionId: copy.id });
    } catch (e) {
      Alert.alert('Not relisted', e instanceof ServerError ? e.message : 'Please try again.');
    }
  };

  const open = (item: MyListing) => {
    if (item.state === 'Draft') navigation.navigate('SellWizard', { listingId: item.id });
    else if ((item.state === 'Sold' || item.state === 'Completed' || item.state === 'AwaitingPayment') && item.orderId)
      navigation.navigate('MainTabs', { screen: 'MyPurchases' });
    else navigation.navigate('PartDetails', { auctionId: item.id });
  };

  const shown = items.filter(i => inFilter(i.state, filter));

  return (
    <SafeAreaView style={styles.container}>
      <Header title="My Listings" onBack={() => navigation.goBack()} />
      <FlatList
        horizontal
        data={FILTERS as unknown as string[]}
        keyExtractor={f => f}
        style={styles.filters}
        contentContainerStyle={styles.filtersContent}
        showsHorizontalScrollIndicator={false}
        renderItem={({ item: f }) => (
          <TouchableOpacity style={[styles.pill, filter === f && styles.pillActive]} onPress={() => setFilter(f as any)}>
            <Text style={[styles.pillText, filter === f && styles.pillTextActive]}>
              {label(f)} ({items.filter(i => inFilter(i.state, f as (typeof FILTERS)[number])).length})
            </Text>
          </TouchableOpacity>
        )}
      />
      <FlatList
        data={shown}
        keyExtractor={i => i.id}
        contentContainerStyle={styles.list}
        refreshControl={<RefreshControl refreshing={loading} onRefresh={load} tintColor={COLORS.primaryGlow} />}
        ListEmptyComponent={!loading ? <Text style={styles.empty}>Nothing here yet. Tap “Sell” to list a part or a vehicle.</Text> : undefined}
        renderItem={({ item }) => (
          <TouchableOpacity style={styles.card} onPress={() => open(item)} activeOpacity={0.9}>
            {item.imageUrl ? <Image source={{ uri: item.imageUrl }} style={styles.thumb} /> : <View style={styles.thumb} />}
            <View style={styles.body}>
              <Badge label={label(item.state).toUpperCase()} variant={VARIANT[item.state]} size="sm" />
              <Text style={styles.title} numberOfLines={2}>{item.title || 'Untitled draft'}</Text>
              {item.saleType === 'FixedPrice' ? (
                <Text style={styles.meta}>
                  Direct sale {money(item.partPrice)} · {item.quantitySold ?? 0} sold
                  {item.state === 'Live' ? ` · ${item.quantityAvailable ?? 0} left` : ''}
                </Text>
              ) : (
                <Text style={styles.meta}>
                  {item.currentHighBid ? `Current bid ${money(item.currentHighBid)}` : `Part price ${money(item.partPrice)}`} · {item.bidCount}{' '}
                  {item.bidCount === 1 ? 'bid' : 'bids'}
                  {item.state === 'Live' ? ' · ⏱ ' : ''}
                  {item.state === 'Live' ? <Countdown end={item.auctionEnd} /> : null}
                </Text>
              )}
              {item.state === 'EndedNoBids' || item.state === 'UnsoldNotPaid' ? (
                <TouchableOpacity style={styles.relist} onPress={() => relist(item)}>
                  <Text style={styles.relistText}>↻ Relist</Text>
                </TouchableOpacity>
              ) : null}
            </View>
          </TouchableOpacity>
        )}
      />
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  filters: { flexGrow: 0 },
  filtersContent: { padding: SPACING.md, gap: SPACING.xs },
  pill: { backgroundColor: COLORS.card, paddingHorizontal: SPACING.md, paddingVertical: SPACING.xs + 2, borderRadius: RADIUS.full, borderWidth: 1, borderColor: COLORS.border },
  pillActive: { backgroundColor: COLORS.primary, borderColor: COLORS.primaryGlow },
  pillText: { color: COLORS.textSecondary, fontSize: 12, fontWeight: '600' },
  pillTextActive: { color: COLORS.text, fontWeight: '800' },
  list: { paddingHorizontal: SPACING.md, paddingBottom: SPACING.xxl },
  empty: { color: COLORS.textMuted, textAlign: 'center', marginTop: SPACING.xl, fontSize: 14 },
  card: { flexDirection: 'row', gap: SPACING.md, backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, borderWidth: 1, borderColor: COLORS.border, padding: SPACING.sm, marginBottom: SPACING.sm },
  thumb: { width: 84, height: 84, borderRadius: RADIUS.md, backgroundColor: COLORS.card },
  body: { flex: 1, gap: 4, alignItems: 'flex-start' },
  title: { color: COLORS.text, fontSize: 14, fontWeight: '700' },
  meta: { color: COLORS.textMuted, fontSize: 12 },
  relist: { marginTop: 4, backgroundColor: COLORS.primary, paddingHorizontal: SPACING.md, paddingVertical: 6, borderRadius: RADIUS.md },
  relistText: { color: COLORS.text, fontWeight: '800', fontSize: 12 },
});
