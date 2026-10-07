import React, { useState, useEffect } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, RefreshControl, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { CompositeScreenProps } from '@react-navigation/native';
import { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { MainTabParamList, RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { ApiService } from '../services/api';
import { Auction } from '../types/models';
import { AuctionCard } from '../components/AuctionCard';
import { Badge } from '../components/Badge';
import { useAuth } from '../auth/AuthContext';
import { LocationSelector } from '../components/LocationSelector';

type Props = CompositeScreenProps<
  BottomTabScreenProps<MainTabParamList, 'Home'>,
  NativeStackScreenProps<RootStackParamList>
>;

export const HomeScreen: React.FC<Props> = ({ navigation }) => {
  const { user, logout } = useAuth();
  const [auctions, setAuctions] = useState<Auction[]>([]);
  const [loading, setLoading] = useState(false);
  const [selectedVehicle, setSelectedVehicle] = useState('All Garage');

  const loadData = async () => {
    setLoading(true);
    const data = await ApiService.getAuctions();
    setAuctions(data);
    setLoading(false);
  };

  useEffect(() => {
    loadData();
  }, []);

  const handleAccountPress = () => {
    if (!user) {
      navigation.navigate('Login');
      return;
    }
    Alert.alert('Log out', `Signed in as ${user.email}`, [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Log out', style: 'destructive', onPress: () => logout() },
    ]);
  };

  return (
    <SafeAreaView style={styles.container}>
      {/* Top Banner Bar */}
      <View style={styles.topHeader}>
        <View>
          <Text style={styles.brandTitle}>OKOH PARTS ⚡</Text>
          <Text style={styles.portSub}>Pickup Port: <Text style={styles.portBold}>Lagos Apapa Port</Text></Text>
          {/* Signed-in user (tap to log out) or guest (tap to log in) */}
          <TouchableOpacity onPress={handleAccountPress} hitSlop={8} activeOpacity={0.7}>
            <Text style={styles.userLine}>
              👤 {user ? user.fullName : 'Guest'} · <Text style={styles.portBold}>{user ? 'Log out' : 'Log in'}</Text>
            </Text>
          </TouchableOpacity>
        </View>
        <TouchableOpacity 
          style={styles.requestBtn} 
          onPress={() => navigation.navigate('OrderPartRequest', {})}
          activeOpacity={0.8}
        >
          <Text style={styles.requestBtnText}>+ Custom Order</Text>
        </TouchableOpacity>
      </View>

      <ScrollView 
        contentContainerStyle={styles.scroll}
        refreshControl={<RefreshControl refreshing={loading} onRefresh={loadData} tintColor={COLORS.primaryGlow} />}
      >
        {/* Planned Containers Schedule Banner */}
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

        {/* Garage Filter Shortcuts */}
        <View style={styles.garageBar}>
          <Text style={styles.sectionHeader}>FILTER BY YOUR GARAGE</Text>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.filterScroll}>
            {['All Garage', '2018 BMW 330i', '2021 BMW M4', 'S58 Engines', 'Headlights'].map(item => (
              <TouchableOpacity
                key={item}
                style={[styles.filterChip, selectedVehicle === item && styles.filterChipActive]}
                onPress={() => setSelectedVehicle(item)}
              >
                <Text style={[styles.filterText, selectedVehicle === item && styles.filterTextActive]}>
                  {item}
                </Text>
              </TouchableOpacity>
            ))}
          </ScrollView>
        </View>

        {/* Which saved location the parts list is for (signed-in users) */}
        {user ? (
          <View style={styles.locationBar}>
            <LocationSelector onManage={() => navigation.navigate('ManageLocations')} />
          </View>
        ) : null}

        {/* Live Auctions Ticker Section */}
        <View style={styles.sectionRow}>
          <Text style={styles.sectionTitle}>🔥 LIVE BMW AUCTIONS</Text>
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
          />
        ))}

        {/* Quick Custom Request Floating CTA */}
        <View style={styles.partRequestBox}>
          <Text style={styles.boxTitle}>Can't find your specific BMW part?</Text>
          <Text style={styles.boxDesc}>Submit a custom part request and check instant container packing feasibility.</Text>
          <TouchableOpacity 
            style={styles.boxBtn}
            onPress={() => navigation.navigate('OrderPartRequest', {})}
            activeOpacity={0.8}
          >
            <Text style={styles.boxBtnText}>REQUEST A BMW PART NOW</Text>
          </TouchableOpacity>
        </View>
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
  brandTitle: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '900',
    letterSpacing: 1,
  },
  portSub: {
    color: COLORS.textMuted,
    fontSize: 11,
    marginTop: 2,
  },
  portBold: {
    color: COLORS.primaryGlow,
    fontWeight: '700',
  },
  userLine: {
    color: COLORS.textMuted,
    fontSize: 11,
    marginTop: 4,
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
  locationBar: {
    marginBottom: SPACING.md,
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
