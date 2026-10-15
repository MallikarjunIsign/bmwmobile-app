import React, { useCallback, useEffect, useState } from 'react';
import { View, Text, StyleSheet, FlatList, RefreshControl, TouchableOpacity, Modal, Image } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { RootStackParamList } from '../types/navigation';
import type { Auction } from '../types/models';
import type { PublicProfile, Review, SellerRatingSummary } from '../types/review';
import type { CountryCode } from '../types/location';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { AuctionCard } from '../components/AuctionCard';
import { ReviewCard } from '../components/ReviewCard';
import { Stars } from '../components/Stars';
import { MarketApi } from '../services/marketApi';
import { ReviewApi } from '../services/reviewApi';
import { isCurrentUser } from '../auth/session';
import { useLocations } from '../location/LocationContext';
import { withShippingEstimates } from '../hooks/useListingSearch';
import { countryName } from '../utils/geo';
import { memberSince, NEW_SELLER_BELOW } from '../utils/rating';

type Props = NativeStackScreenProps<RootStackParamList, 'SellerProfile'>;

/**
 * A seller's profile: who (username, member since, city / country), ⭐ average + number of ratings, the 5★…1★
 * breakdown, Communication / Accuracy / Delivery averages, completed sales, the latest reviews (with the item), and
 * everything they have live right now.
 */
export const SellerProfileScreen: React.FC<Props> = ({ route, navigation }) => {
  const { seller, sellerName } = route.params;
  const [profile, setProfile] = useState<PublicProfile | null>(null);
  const [rating, setRating] = useState<SellerRatingSummary | null>(null);
  const [reviews, setReviews] = useState<Review[]>([]);
  const [page, setPage] = useState(1);
  const [moreReviews, setMoreReviews] = useState(false);
  const [listings, setListings] = useState<Auction[]>([]);
  const [loading, setLoading] = useState(true);
  const [photo, setPhoto] = useState<string | null>(null);

  const { near, country } = useLocations();
  const own = isCurrentUser(seller);
  const load = useCallback(async () => {
    setLoading(true);
    const [p, r, l] = await Promise.all([
      ReviewApi.publicProfile(seller).catch(() => null),
      ReviewApi.seller(seller).catch(() => null),
      // A buyer sees what they could get (pickup-only inside their radius, shipping in their country), with the
      // shipping charges; your own profile shows all your listings.
      (own
        ? MarketApi.search({ seller, sort: 'newest', pageSize: 50 })
        : MarketApi.search({ seller, sort: 'newest', pageSize: 50 }, near, country))
        .then(res => res.results.map(MarketApi.hitToAuction))
        .then(list => (own ? list : withShippingEstimates(list, near, country)))
        .catch(() => [] as Auction[]),
    ]);
    setProfile(p);
    setRating(r);
    setReviews(r?.latestReviews ?? []);
    setMoreReviews((r?.count ?? 0) > (r?.latestReviews.length ?? 0));
    setPage(1);
    setListings(l);
    setLoading(false);
  }, [seller, own, near, country]);

  useEffect(() => {
    load();
  }, [load]);

  const loadMore = async () => {
    const next = await ReviewApi.sellerReviews(seller, page === 1 ? 1 : page + 1).catch(() => []);
    // Page 1 (20) replaces the 5 latest; later pages are added.
    setReviews(list => (page === 1 ? next : [...list, ...next]));
    setPage(p => (p === 1 ? 2 : p + 1));
    setMoreReviews(next.length === 20);
  };

  const mine = isCurrentUser(seller);
  const name = mine ? 'You' : profile?.displayName || sellerName || seller;
  const count = rating?.count ?? 0;
  const place = [profile?.city, countryName(profile?.countryCode as CountryCode)].filter(Boolean).join(', ');
  const total = Math.max(count, 1);

  const header = (
    <View>
      <View style={styles.card}>
        <Text style={styles.name}>{name}</Text>
        <Text style={styles.username}>@{seller}</Text>
        <Text style={styles.meta}>{[memberSince(profile?.memberSince), place].filter(Boolean).join(' · ')}</Text>

        <View style={styles.summary}>
          <View style={styles.big}>
            <Text style={styles.average}>{count ? rating!.average?.toFixed(1) : '–'}</Text>
            <Stars value={rating?.average ?? 0} size={16} />
            <Text style={styles.count}>{count} {count === 1 ? 'rating' : 'ratings'}</Text>
            {count < NEW_SELLER_BELOW ? <Text style={styles.newSeller}>New seller</Text> : null}
          </View>
          <View style={styles.bars}>
            {(rating?.breakdown ?? [0, 0, 0, 0, 0]).map((n, i) => (
              <View key={i} style={styles.barRow} accessibilityLabel={`${5 - i} stars: ${n}`}>
                <Text style={styles.barLabel}>{5 - i}★</Text>
                <View style={styles.barTrack}>
                  <View style={[styles.barFill, { width: `${(n / total) * 100}%` }]} />
                </View>
                <Text style={styles.barCount}>{n}</Text>
              </View>
            ))}
          </View>
        </View>

        <View style={styles.stats}>
          {[
            ['Communication', rating?.communicationAverage],
            ['Accuracy', rating?.accuracyAverage],
            ['Delivery', rating?.deliveryAverage],
          ].map(([label, value]) => (
            <View key={label as string} style={styles.stat}>
              <Text style={styles.statValue}>{value ? `${(value as number).toFixed(1)}★` : '–'}</Text>
              <Text style={styles.statLabel}>{label as string}</Text>
            </View>
          ))}
          <View style={styles.stat}>
            <Text style={styles.statValue}>{rating?.completedSales ?? 0}</Text>
            <Text style={styles.statLabel}>Sales</Text>
          </View>
        </View>
      </View>

      <Text style={styles.section}>REVIEWS</Text>
      {reviews.length === 0 && !loading ? <Text style={styles.empty}>No reviews yet.</Text> : null}
      {reviews.map(r => (
        <ReviewCard key={r.id} review={r} showItem onOpenPhoto={setPhoto}
          onChange={updated => setReviews(list => list.map(x => (x.id === updated.id ? updated : x)))} />
      ))}
      {moreReviews ? (
        <TouchableOpacity onPress={loadMore} style={styles.more}>
          <Text style={styles.link}>Show more reviews</Text>
        </TouchableOpacity>
      ) : null}

      <Text style={styles.section}>{mine ? 'YOUR LIVE LISTINGS' : 'LIVE LISTINGS'} ({listings.length})</Text>
      {listings.length === 0 && !loading ? <Text style={styles.empty}>Nothing for sale right now.</Text> : null}
    </View>
  );

  return (
    <SafeAreaView style={styles.container}>
      <Header title={mine ? 'Your profile' : name} subtitle="Seller profile" onBack={() => navigation.goBack()} />
      <FlatList
        data={listings}
        keyExtractor={item => item.id}
        contentContainerStyle={styles.list}
        refreshControl={<RefreshControl refreshing={loading} onRefresh={load} tintColor={COLORS.primaryGlow} />}
        ListHeaderComponent={header}
        renderItem={({ item }) => (
          <AuctionCard
            auction={item}
            onPress={() => navigation.navigate('PartDetails', { auctionId: item.id, auction: item })}
            onBidPress={() => navigation.navigate('PlaceBidModal', { auction: item })}
          />
        )}
      />
      <Modal visible={!!photo} transparent animationType="fade" onRequestClose={() => setPhoto(null)}>
        <TouchableOpacity style={styles.viewer} activeOpacity={1} onPress={() => setPhoto(null)} accessibilityLabel="Close photo">
          {photo ? <Image source={{ uri: photo }} style={styles.viewerImage} resizeMode="contain" /> : null}
        </TouchableOpacity>
      </Modal>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  list: { padding: SPACING.md, paddingBottom: SPACING.xxl },
  card: { backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, borderWidth: 1, borderColor: COLORS.border, padding: SPACING.md, marginBottom: SPACING.md },
  name: { color: COLORS.text, fontSize: 20, fontWeight: '900' },
  username: { color: COLORS.textMuted, fontSize: 13 },
  meta: { color: COLORS.textSecondary, fontSize: 12, marginTop: 4 },
  summary: { flexDirection: 'row', gap: SPACING.md, marginTop: SPACING.md, alignItems: 'center' },
  big: { alignItems: 'center', width: 96 },
  average: { color: COLORS.text, fontSize: 36, fontWeight: '900' },
  count: { color: COLORS.textMuted, fontSize: 12, marginTop: 2 },
  newSeller: { color: COLORS.badgeText, fontSize: 11, fontWeight: '800', marginTop: 4 },
  bars: { flex: 1, gap: 4 },
  barRow: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  barLabel: { color: COLORS.textSecondary, fontSize: 12, width: 24 },
  barTrack: { flex: 1, height: 8, borderRadius: 4, backgroundColor: COLORS.card, overflow: 'hidden' },
  barFill: { height: 8, backgroundColor: COLORS.warningGlow },
  barCount: { color: COLORS.textMuted, fontSize: 11, width: 24, textAlign: 'right' },
  stats: { flexDirection: 'row', justifyContent: 'space-between', marginTop: SPACING.md, borderTopWidth: 1, borderTopColor: COLORS.border, paddingTop: SPACING.sm },
  stat: { alignItems: 'center', flex: 1 },
  statValue: { color: COLORS.text, fontSize: 16, fontWeight: '900' },
  statLabel: { color: COLORS.textMuted, fontSize: 11 },
  section: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1, marginTop: SPACING.md, marginBottom: SPACING.sm },
  empty: { color: COLORS.textMuted, fontSize: 13, marginBottom: SPACING.md },
  more: { alignItems: 'center', paddingVertical: SPACING.sm },
  link: { color: COLORS.primaryGlow, fontSize: 13, fontWeight: '800' },
  viewer: { flex: 1, backgroundColor: 'rgba(0,0,0,0.92)', justifyContent: 'center' },
  viewerImage: { width: '100%', height: '80%' },
});
