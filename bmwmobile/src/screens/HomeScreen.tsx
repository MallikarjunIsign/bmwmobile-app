import React from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, RefreshControl } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { CompositeScreenProps } from '@react-navigation/native';
import { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { MainTabParamList, RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { AuctionCard } from '../components/AuctionCard';
import { Badge } from '../components/Badge';
import { RoleBadge } from '../components/RoleBadge';
import { useAuth } from '../auth/AuthContext';
import { useLocations } from '../location/LocationContext';
import { FEATURES } from '../config';
import { LocationBanner } from '../components/LocationBanner';
import { NoPartsFound } from '../components/NoPartsFound';
import { BrowseFilters } from '../components/BrowseFilters';
import { CategoryTiles } from '../components/CategoryTiles';
import { useListingSearch } from '../hooks/useListingSearch';
import { categoryChip, useBrowse } from '../search/BrowseContext';
import { BrowseSearchBox } from '../search/BrowseSearchBox';
import { REGIONS, formatRadius, locationText } from '../utils/geo';
import { useChat } from '../chat/ChatContext';

type Props = CompositeScreenProps<
  BottomTabScreenProps<MainTabParamList, 'Home'>,
  NativeStackScreenProps<RootStackParamList>
>;

export const HomeScreen: React.FC<Props> = ({ navigation }) => {
  const { user } = useAuth();
  const { unread } = useChat();
  // Listings within 60 km / 60 mi of the location (GPS or a chosen address), or country-wide while location is off.
  const { country, location, locationOff, near } = useLocations();
  // Parts / Vehicles and All / Auction / Direct sale: shared with Market, so they apply to every search too.
  const browse = useBrowse();
  const { itemType, saleType } = browse;
  const parts = itemType === 'Part';
  const make = parts ? undefined : browse.filters.make;

  // The SAME search as Market (search box chips + every filter), so the counts and the list match Market's.
  const { results: auctions, facets, total, loading, reload: loadData } = useListingSearch(browse.input);
  const makeFacets = facets.make ?? [];

  // A category tile works like a category suggestion: a chip in the search box, the list below updates at once.
  const openCategory = (category: string) => browse.applyChips([categoryChip(category)]);
  const setMake = (value?: string) => browse.setFilters(f => ({ ...f, make: value, model: undefined }));

  // Market shows the same results with more filters (part types, models, the filter sheet).
  const seeAll = () => navigation.navigate('Marketplace');

  const where = near && location ? 'near you' : `in ${REGIONS[country].name}`;

  return (
    <SafeAreaView style={styles.container}>
      {/* Top Banner Bar */}
      <View style={styles.topHeader}>
        <View style={styles.headerLeft}>
          <Text style={styles.brandTitle}>SHOPCARPARTS ⚡</Text>
          {/* Signed-in user: tap for Profile (locations, garage, selling, log out) */}
          <TouchableOpacity
            onPress={() => navigation.navigate('Profile')}
            hitSlop={8}
            activeOpacity={0.7}
            accessibilityRole="button"
            accessibilityLabel="Profile"
          >
            <Text style={styles.userLine} numberOfLines={1}>
              👤 {user?.fullName} · <Text style={styles.linkBold}>Profile</Text>
            </Text>
          </TouchableOpacity>
          {/* [SELLER] / [BUYER] then the location (GPS or a chosen address) – tap the location to change it.
              A long location is shortened with "…", never the badge. */}
          <View style={styles.locationRow}>
            <RoleBadge role={user?.role} />
            <TouchableOpacity
              style={styles.locationTouch}
              onPress={() => navigation.navigate('Locations')}
              hitSlop={8}
              activeOpacity={0.7}
              accessibilityRole="button"
              accessibilityLabel="Change location"
            >
              <Text style={styles.locationLine} numberOfLines={1} ellipsizeMode="tail">
                📍 {locationText(location) ?? (locationOff ? 'Location off – set your location' : 'Finding your location…')}
              </Text>
            </TouchableOpacity>
          </View>
        </View>
        <View style={styles.headerRight}>
          {/* Messages (buyer–seller chat) with the unread count. */}
          <TouchableOpacity
            style={styles.messagesBtn}
            onPress={() => navigation.navigate('Inbox')}
            activeOpacity={0.8}
            accessibilityRole="button"
            accessibilityLabel={unread > 0 ? `Messages, ${unread} unread` : 'Messages'}
          >
            <Text style={styles.messagesIcon}>💬</Text>
            {unread > 0 ? (
              <View style={styles.unreadBadge}>
                <Text style={styles.unreadText}>{unread > 99 ? '99+' : unread}</Text>
              </View>
            ) : null}
          </TouchableOpacity>
          <TouchableOpacity 
            style={styles.requestBtn} 
            onPress={() => navigation.navigate('OrderPartRequest', {})}
            activeOpacity={0.8}
          >
            <Text style={styles.requestBtnText}>+ Custom Order</Text>
          </TouchableOpacity>
        </View>
      </View>

      {/* "handled": a tap on a search suggestion must reach it. Without it the first tap only closes the keyboard and
          the suggestion is lost (the Home search bug). */}
      <ScrollView
        contentContainerStyle={styles.scroll}
        keyboardShouldPersistTaps="handled"
        refreshControl={<RefreshControl refreshing={loading} onRefresh={loadData} tintColor={COLORS.primaryGlow} />}
      >
        {/* Location denied / GPS off */}
        <LocationBanner />

        <View style={styles.searchWrap}>
          <BrowseSearchBox />
        </View>

        {/* Step 1 – what: Parts / Vehicles. Step 2 – how: All / Auction / Direct sale. */}
        <View style={styles.browseWrap}>
          <BrowseFilters />
        </View>

        {/* Planned Containers Schedule Banner (hidden while FEATURES.containers is off) */}
        {FEATURES.containers ? (
          <TouchableOpacity 
            style={styles.containerBanner}
            onPress={() => navigation.navigate('PlannedContainers')}
            activeOpacity={0.9}
          >
            <View style={styles.bannerLeft}>
              <Text style={styles.bannerTag}>🚢 CONTAINER SCHEDULE</Text>
              <Text style={styles.bannerTitle}>Container CN-2026-09</Text>
              <Text style={styles.bannerDetail}>Bremerhaven → Lagos Apapa • Cutoff in 12 Days</Text>
            </View>
            <View style={styles.bannerRight}>
              <Badge label="68% FULL" variant="warning" size="sm" />
              <Text style={styles.bannerArrow}>View →</Text>
            </View>
          </TouchableOpacity>
        ) : null}

        {parts ? (
          <>
            {/* Part categories with the number of parts available (location radius + Auction / Direct sale). */}
            <Text style={styles.sectionHeader}>SHOP BY CATEGORY</Text>
            <View style={styles.tiles}>
              <CategoryTiles categories={facets.category ?? []} selected={browse.filters.category} onPress={openCategory} />
            </View>
          </>
        ) : (
          <>
            {/* All available vehicles: the total and make chips with counts. */}
            <Text style={styles.vehicleTotal}>
              {loading && !total ? 'Finding vehicles…' : `${total} ${total === 1 ? 'vehicle' : 'vehicles'} ${where}`}
              {near && location ? <Text style={styles.vehicleWithin}> · within {formatRadius(location.radiusKm, country)}</Text> : null}
            </Text>
            {makeFacets.length > 0 ? (
              <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.filterScroll} keyboardShouldPersistTaps="handled">
                <TouchableOpacity style={[styles.filterChip, !make && styles.filterChipActive]} onPress={() => setMake(undefined)}
                  accessibilityState={{ selected: !make }}>
                  <Text style={[styles.filterText, !make && styles.filterTextActive]}>All makes</Text>
                </TouchableOpacity>
                {makeFacets.map(m => (
                  <TouchableOpacity key={m.value} style={[styles.filterChip, make === m.value && styles.filterChipActive]}
                    onPress={() => setMake(make === m.value ? undefined : m.value)} accessibilityState={{ selected: make === m.value }}>
                    <Text style={[styles.filterText, make === m.value && styles.filterTextActive]}>{m.value} ({m.count})</Text>
                  </TouchableOpacity>
                ))}
              </ScrollView>
            ) : null}
          </>
        )}

        {/* Live listings for the filters above */}
        <View style={styles.sectionRow}>
          <Text style={styles.sectionTitle}>{parts ? '🔥 LIVE LISTINGS' : make ? `🚗 ${make.toUpperCase()}` : '🚗 ALL VEHICLES'}</Text>
          <View style={styles.sectionActions}>
            {browse.hasAnything ? (
              <TouchableOpacity onPress={browse.clearAll} accessibilityLabel="Clear all" hitSlop={8}>
                <Text style={styles.clearAll}>Clear all</Text>
              </TouchableOpacity>
            ) : null}
            <TouchableOpacity onPress={seeAll}>
              <Text style={styles.seeAll}>See All ({total}) →</Text>
            </TouchableOpacity>
          </View>
        </View>

        {auctions.map(item => (
          <AuctionCard
            key={item.id}
            auction={item}
            onPress={() => navigation.navigate('PartDetails', { auctionId: item.id, auction: item })}
            onBidPress={() => navigation.navigate('PlaceBidModal', { auction: item })}
            onSellerPress={() => navigation.navigate('SellerProfile', { seller: item.seller, sellerName: item.sellerName })}
          />
        ))}

        {/* Nothing to show: offer a custom order */}
        {!loading && auctions.length === 0 ? (
          <NoPartsFound onCreateOrder={() => navigation.navigate('OrderPartRequest', {})} />
        ) : (
          /* Quick Custom Request Floating CTA */
          <View style={styles.partRequestBox}>
            <Text style={styles.boxTitle}>Can't find your part?</Text>
            <Text style={styles.boxDesc}>Create a custom order and get notified when the part is available.</Text>
            <TouchableOpacity 
              style={styles.boxBtn}
              onPress={() => navigation.navigate('OrderPartRequest', {})}
              activeOpacity={0.8}
            >
              <Text style={styles.boxBtnText}>CREATE A CUSTOM ORDER</Text>
            </TouchableOpacity>
          </View>
        )}
      </ScrollView>
    </SafeAreaView>
  );
};
const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.background,
  },
  topHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.sm + 4,
    backgroundColor: COLORS.surface,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.border,
  },
  headerLeft: {
    flex: 1,
    marginRight: SPACING.sm,
  },
  brandTitle: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '900',
    letterSpacing: 1,
  },
  linkBold: {
    color: COLORS.primaryGlow,
    fontWeight: '700',
  },
  locationRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginTop: 4,
  },
  locationTouch: {
    flex: 1,
    minWidth: 0,
  },
  locationLine: {
    color: COLORS.textSecondary,
    fontSize: 12,
    fontWeight: '600',
  },
  userLine: {
    color: COLORS.text,
    fontSize: 12,
    fontWeight: '600',
    marginTop: 4,
  },
  headerRight: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: SPACING.sm,
  },
  messagesBtn: {
    width: 40,
    height: 40,
    borderRadius: 20,
    backgroundColor: COLORS.card,
    borderWidth: 1,
    borderColor: COLORS.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  messagesIcon: {
    fontSize: 18,
  },
  unreadBadge: {
    position: 'absolute',
    top: -4,
    right: -4,
    minWidth: 18,
    height: 18,
    borderRadius: 9,
    backgroundColor: COLORS.danger,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 4,
  },
  unreadText: {
    color: COLORS.text,
    fontSize: 10,
    fontWeight: '900',
  },
  requestBtn: {
    backgroundColor: COLORS.primary,
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.xs + 4,
    borderRadius: RADIUS.full,
  },
  requestBtnText: {
    color: COLORS.text,
    fontSize: 12,
    fontWeight: '800',
  },
  searchWrap: {
    marginBottom: SPACING.md,
    zIndex: 10,
  },
  browseWrap: {
    marginBottom: SPACING.md,
  },
  tiles: {
    marginBottom: SPACING.md,
  },
  vehicleTotal: {
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '800',
    marginBottom: SPACING.sm,
  },
  vehicleWithin: {
    color: COLORS.textMuted,
    fontSize: 12,
    fontWeight: '600',
  },
  scroll: {
    padding: SPACING.md,
    paddingBottom: SPACING.xxl,
  },
  containerBanner: {
    backgroundColor: COLORS.card,
    borderRadius: RADIUS.lg,
    padding: SPACING.md,
    borderWidth: 1,
    borderColor: COLORS.primaryGlow,
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: SPACING.md,
  },
  bannerLeft: {
    flex: 1,
  },
  bannerTag: {
    color: COLORS.primaryGlow,
    fontSize: 10,
    fontWeight: '800',
    letterSpacing: 1,
  },
  bannerTitle: {
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '800',
    marginTop: 2,
  },
  bannerDetail: {
    color: COLORS.textMuted,
    fontSize: 12,
    marginTop: 4,
  },
  bannerRight: {
    alignItems: 'flex-end',
    gap: SPACING.xs,
  },
  bannerArrow: {
    color: COLORS.primaryGlow,
    fontWeight: '700',
    fontSize: 13,
  },
  sectionHeader: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: SPACING.xs,
  },
  filterScroll: {
    flexDirection: 'row',
    marginBottom: SPACING.md,
  },
  filterChip: {
    backgroundColor: COLORS.card,
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.xs + 2,
    borderRadius: RADIUS.full,
    borderWidth: 1,
    borderColor: COLORS.border,
    marginRight: SPACING.xs,
  },
  filterChipActive: {
    backgroundColor: COLORS.primary,
    borderColor: COLORS.primaryGlow,
  },
  filterText: {
    color: COLORS.textSecondary,
    fontSize: 12,
    fontWeight: '600',
  },
  filterTextActive: {
    color: COLORS.text,
    fontWeight: '800',
  },
  sectionRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: SPACING.sm,
    marginTop: SPACING.xs,
  },
  sectionTitle: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '800',
    letterSpacing: 0.5,
  },
  sectionActions: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: SPACING.md,
  },
  clearAll: {
    color: COLORS.danger,
    fontSize: 12,
    fontWeight: '700',
  },
  seeAll: {
    color: COLORS.primaryGlow,
    fontSize: 13,
    fontWeight: '700',
  },
  partRequestBox: {
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.lg,
    padding: SPACING.lg,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
    marginTop: SPACING.md,
    alignItems: 'center',
  },
  boxTitle: {
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '800',
    textAlign: 'center',
  },
  boxDesc: {
    color: COLORS.textMuted,
    fontSize: 13,
    textAlign: 'center',
    marginTop: SPACING.xs,
    marginBottom: SPACING.md,
    lineHeight: 18,
  },
  boxBtn: {
    backgroundColor: COLORS.cardSecondary,
    paddingHorizontal: SPACING.lg,
    paddingVertical: SPACING.md,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.primary,
    width: '100%',
    alignItems: 'center',
  },
  boxBtnText: {
    color: COLORS.primaryGlow,
    fontSize: 13,
    fontWeight: '900',
    letterSpacing: 1,
  },
});
