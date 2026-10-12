import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, FlatList, TouchableOpacity, Image, RefreshControl } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { CompositeScreenProps, useFocusEffect } from '@react-navigation/native';
import { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { MainTabParamList, RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { MarketApi } from '../services/marketApi';
import { mapRawDtoToAuction } from '../services/api';
import { useLiveEvent } from '../services/notifications';
import { Badge } from '../components/Badge';
import { useMoney } from '../location/useMoney';
import { Countdown } from '../components/Countdown';
import type { MyBid } from '../types/listing';
import type { Auction } from '../types/models';

type Props = CompositeScreenProps<
  BottomTabScreenProps<MainTabParamList, 'MyBids'>,
  NativeStackScreenProps<RootStackParamList>
>;

const TABS = ['Winning', 'Outbid', 'Won', 'Lost'] as const;
const VARIANT = { Winning: 'success', Outbid: 'warning', Won: 'success', Lost: 'dark' } as const;

type Row = MyBid & { auction?: Auction };

/** My Bids: Winning / Outbid while live; Won / Lost once ended. Updates live. */
export const MyBidsScreen: React.FC<Props> = ({ navigation }) => {
  const money = useMoney();
  const [tab, setTab] = useState<(typeof TABS)[number]>('Winning');
  const [rows, setRows] = useState<Row[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const bids = await MarketApi.myBids();
      const details = await Promise.all(
        bids.map(b => MarketApi.listingDetail(b.auctionId).then(mapRawDtoToAuction).catch(() => undefined)),
      );
      setRows(bids.map((b, i) => ({ ...b, auction: details[i] })));
      setError(null);
    } catch (e: any) {
      setError(e?.message ?? "Couldn't load your bids");
    } finally {
      setLoading(false);
    }
  }, []);

  useFocusEffect(useCallback(() => { load(); }, [load]));
  useLiveEvent('BidPlaced', (m: { auctionId?: string }) => rows.some(r => r.auctionId === m?.auctionId) && load());
  useLiveEvent('AuctionFinished', (m: { auctionId?: string }) => rows.some(r => r.auctionId === m?.auctionId) && load());

  const shown = rows.filter(r => r.state === tab);

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.title}>My Bids</Text>
      </View>
      <View style={styles.tabBar}>
        {TABS.map(t => (
          <TouchableOpacity key={t} style={[styles.tab, tab === t && styles.tabActive]} onPress={() => setTab(t)}>
            <Text style={[styles.tabText, tab === t && styles.tabTextActive]}>
              {t} ({rows.filter(r => r.state === t).length})
            </Text>
          </TouchableOpacity>
        ))}
      </View>
      {error ? <Text style={styles.error}>{error}</Text> : null}
      <FlatList
        data={shown}
        keyExtractor={r => r.auctionId}
        contentContainerStyle={styles.list}
        refreshControl={<RefreshControl refreshing={loading} onRefresh={load} tintColor={COLORS.primaryGlow} />}
        ListEmptyComponent={!loading ? <Text style={styles.empty}>No {tab.toLowerCase()} bids.</Text> : undefined}
        renderItem={({ item }) => {
          const image = item.auction?.thumbnailUrl ?? item.auction?.item.images[0]?.url;
          const live = item.state === 'Winning' || item.state === 'Outbid';
          return (
            <TouchableOpacity
              style={styles.card}
              onPress={() => navigation.navigate('PartDetails', { auctionId: item.auctionId, auction: item.auction })}
              activeOpacity={0.9}
            >
              {image ? <Image source={{ uri: image }} style={styles.thumb} /> : <View style={styles.thumb} />}
              <View style={styles.body}>
                <Badge label={item.state === 'Winning' ? "YOU'RE THE HIGHEST BIDDER" : item.state.toUpperCase()} variant={VARIANT[item.state]} size="sm" />
                <Text style={styles.itemTitle} numberOfLines={2}>{item.auction?.title ?? 'Listing'}</Text>
                <Text style={styles.meta}>
                  Your bid {money(item.myHighestBid)} · {item.state === 'Won' ? 'Final' : 'Highest'} {money(item.currentHighBid ?? item.myHighestBid)}
                </Text>
                <Text style={styles.meta}>
                  {item.bidCount} bids{live ? ' · ⏱ ' : ''}
                  {live ? <Countdown end={item.auctionEnd} /> : null}
                </Text>
                {item.state === 'Outbid' && item.auction ? (
                  <TouchableOpacity style={styles.action} onPress={() => navigation.navigate('PlaceBidModal', { auction: item.auction! })}>
                    <Text style={styles.actionText}>Bid again</Text>
                  </TouchableOpacity>
                ) : null}
                {item.state === 'Won' ? (
                  <TouchableOpacity style={styles.action} onPress={() => navigation.navigate('MyPurchases')}>
                    <Text style={styles.actionText}>See in Orders</Text>
                  </TouchableOpacity>
                ) : null}
              </View>
            </TouchableOpacity>
          );
        }}
      />
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  header: { padding: SPACING.md, backgroundColor: COLORS.surface, borderBottomWidth: 1, borderBottomColor: COLORS.border },
  title: { color: COLORS.text, fontSize: 18, fontWeight: '800' },
  tabBar: { flexDirection: 'row', backgroundColor: COLORS.surface, borderBottomWidth: 1, borderBottomColor: COLORS.border },
  tab: { flex: 1, paddingVertical: SPACING.sm + 2, alignItems: 'center', borderBottomWidth: 2, borderBottomColor: 'transparent' },
  tabActive: { borderBottomColor: COLORS.primaryGlow },
  tabText: { color: COLORS.textMuted, fontSize: 12, fontWeight: '700' },
  tabTextActive: { color: COLORS.primaryGlow },
  error: { color: COLORS.danger, padding: SPACING.md },
  list: { padding: SPACING.md, paddingBottom: SPACING.xxl },
  empty: { color: COLORS.textMuted, textAlign: 'center', marginTop: SPACING.xl },
  card: { flexDirection: 'row', gap: SPACING.md, backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, borderWidth: 1, borderColor: COLORS.border, padding: SPACING.sm, marginBottom: SPACING.sm },
  thumb: { width: 84, height: 84, borderRadius: RADIUS.md, backgroundColor: COLORS.card },
  body: { flex: 1, gap: 3, alignItems: 'flex-start' },
  itemTitle: { color: COLORS.text, fontSize: 14, fontWeight: '700' },
  meta: { color: COLORS.textMuted, fontSize: 12 },
  action: { marginTop: 4, backgroundColor: COLORS.primary, paddingHorizontal: SPACING.md, paddingVertical: 6, borderRadius: RADIUS.md },
  actionText: { color: COLORS.text, fontWeight: '800', fontSize: 12 },
});
