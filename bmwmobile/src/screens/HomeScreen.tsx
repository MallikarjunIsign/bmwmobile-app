import React, { useMemo, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, RefreshControl, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { CompositeScreenProps } from '@react-navigation/native';
import { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { MainTabParamList, RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { AuctionCard } from '../components/AuctionCard';
import { Badge } from '../components/Badge';
import { useAuth } from '../auth/AuthContext';
import { useLocations } from '../location/LocationContext';
import { FEATURES } from '../config';
import { LocationBanner } from '../components/LocationBanner';
import { NoPartsFound } from '../components/NoPartsFound';
import { SearchBox, rememberSearch, type SearchState } from '../components/SearchBox';
import { garageLabel, useGarage } from '../location/GarageContext';
import { useListingSearch } from '../hooks/useListingSearch';
import { locationText } from '../utils/geo';
import { useChat } from '../chat/ChatContext';

type Props = CompositeScreenProps<
  BottomTabScreenProps<MainTabParamList, 'Home'>,
  NativeStackScreenProps<RootStackParamList>
>;

export const HomeScreen: React.FC<Props> = ({ navigation }) => {
  const { user, logout } = useAuth();
  const { unread } = useChat();
  // Parts within 60 km / 60 mi of the location (GPS or a chosen address), or country-wide while location is off.
  const { country, location, locationOff } = useLocations();
  // My Garage: choosing a vehicle filters this list (and every search) by it.
  const { vehicles, selected: garage, select: selectGarage } = useGarage();
  const [search, setSearch] = useState<SearchState>({ text: '', chips: [] });
  const input = useMemo(() => ({ text: '', chips: [], filters: { sort: 'endingSoon' as const } }), []);
  const { results: auctions, loading, reload: loadData } = useListingSearch(input);

  // One search box: searching opens the Market with the results.
  const submitSearch = (next: SearchState) => {
    rememberSearch(next);
    const text = [...next.chips.map(c => c.label), next.text].filter(Boolean).join(' ');
    if (text) navigation.navigate('Marketplace', { searchTerm: text });
    setSearch({ text: '', chips: [] });
  };

  const handleAccountPress = () => {
    if (!user) return;
    Alert.alert('Log out', `Signed in as ${user.email}`, [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Log out', style: 'destructive', onPress: () => logout() },
    ]);
  };

  return (
    <SafeAreaView style={styles.container}>
      {/* Top Banner Bar */}
      <View style={styles.topHeader}>
        <View style={styles.headerLeft}>
          <Text style={styles.brandTitle}>SHOPCARPARTS ⚡</Text>
          {/* Signed-in user (tap to log out) */}
          <TouchableOpacity onPress={handleAccountPress} hitSlop={8} activeOpacity={0.7}>
            <Text style={styles.userLine} numberOfLines={1}>
              👤 {user?.fullName} · <Text style={styles.linkBold}>Log out</Text>
            </Text>
          </TouchableOpacity>
          {/* The user's location (GPS or a chosen address): just the place and country – tap to change it. */}
          <TouchableOpacity
            onPress={() => navigation.navigate('Locations')}
            hitSlop={8}
            activeOpacity={0.7}
            accessibilityRole="button"
            accessibilityLabel="Change location"
          >
            <Text style={styles.locationLine} numberOfLines={1}>
              {locationText(location) ?? (locationOff ? 'Location off – set your location' : 'Finding your location…')}
            </Text>
          </TouchableOpacity>
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

      <ScrollView 
        contentContainerStyle={styles.scroll}
        refreshControl={<RefreshControl refreshing={loading} onRefresh={loadData} tintColor={COLORS.primaryGlow} />}
      >
        {/* Location denied / GPS off */}
        <LocationBanner />

        <View style={styles.searchWrap}>
          <SearchBox value={search} onChange={setSearch} onSubmit={submitSearch} country={country} />
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

        {/* Garage Filter Shortcuts */}
        <View style={styles.garageBar}>
          <Text style={styles.sectionHeader}>FILTER BY YOUR GARAGE</Text>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.filterScroll}>
            <TouchableOpacity style={[styles.filterChip, !garage && styles.filterChipActive]} onPress={() => selectGarage(null)}>
              <Text style={[styles.filterText, !garage && styles.filterTextActive]}>All vehicles</Text>
            </TouchableOpacity>
            {vehicles.map(v => (
              <TouchableOpacity
                key={v.id}
                style={[styles.filterChip, garage?.id === v.id && styles.filterChipActive]}
                onPress={() => selectGarage(garage?.id === v.id ? null : v.id!)}
              >
                <Text style={[styles.filterText, garage?.id === v.id && styles.filterTextActive]}>🚗 {garageLabel(v)}</Text>
              </TouchableOpacity>
            ))}
            <TouchableOpacity style={styles.filterChip} onPress={() => navigation.navigate('Garage')}>
              <Text style={styles.filterText}>{vehicles.length ? '⚙ My Garage' : '+ Add your vehicle'}</Text>
            </TouchableOpacity>
          </ScrollView>
        </View>

        {/* Live Auctions Ticker Section */}
        <View style={styles.sectionRow}>
          <Text style={styles.sectionTitle}>
            {garage ? `🔥 FOR YOUR ${garage.year} ${garage.model.toUpperCase()}` : '🔥 LIVE LISTINGS'}
          </Text>
          <TouchableOpacity onPress={() => navigation.navigate('Marketplace', {})}>
            <Text style={styles.seeAll}>See All ({auctions.length}) →</Text>
          </TouchableOpacity>
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
  locationLine: {
    color: COLORS.textSecondary,
    fontSize: 12,
    fontWeight: '600',
    marginTop: 4,
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
  garageBar: {
    marginBottom: SPACING.md,
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
