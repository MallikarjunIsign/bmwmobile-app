import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { useFocusEffect } from '@react-navigation/native';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { Badge } from '../components/Badge';
import { useAuth } from '../auth/AuthContext';
import { config } from '../config';
import { isSeller, roleLabel } from '../utils/roles';
import { SellerApplicationApi, type MySellerApplication } from '../services/sellerApplicationApi';

type Props = NativeStackScreenProps<RootStackParamList, 'Profile'>;

const Row = ({ icon, label, hint, onPress }: { icon: string; label: string; hint?: string; onPress: () => void }) => (
  <TouchableOpacity style={styles.row} onPress={onPress} activeOpacity={0.8} accessibilityRole="button" accessibilityLabel={label}>
    <Text style={styles.rowIcon}>{icon}</Text>
    <View style={styles.rowBody}>
      <Text style={styles.rowLabel}>{label}</Text>
      {hint ? <Text style={styles.rowHint}>{hint}</Text> : null}
    </View>
    <Text style={styles.chevron}>›</Text>
  </TouchableOpacity>
);

/**
 * The signed-in user: who they are and their role, saved locations / addresses and My Garage.
 * Sellers also get My Listings, Sell and their public seller profile with reviews; buyers get "Apply to sell" (a form
 * the team reviews) and see where their request is.
 */
export const ProfileScreen: React.FC<Props> = ({ navigation }) => {
  const { user, logout } = useAuth();
  const seller = isSeller(user);
  // The buyer's latest "Want to sell?" request (refreshed each time Profile opens).
  const [application, setApplication] = useState<MySellerApplication | null>(null);
  useFocusEffect(
    useCallback(() => {
      if (seller) return;
      let alive = true;
      SellerApplicationApi.mine().then(a => alive && setApplication(a)).catch(() => undefined);
      return () => {
        alive = false;
      };
    }, [seller]),
  );
  if (!user) return null;
  const pending = application && (application.status === 'New' || application.status === 'InReview');
  const sentOn = application ? new Date(application.createdAt).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' }) : '';

  const confirmLogout = () =>
    Alert.alert('Log out', `Signed in as ${user.email}`, [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Log out', style: 'destructive', onPress: () => logout() },
    ]);

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Profile" onBack={() => navigation.goBack()} />
      <ScrollView contentContainerStyle={styles.scroll}>
        <View style={styles.card}>
          <View style={styles.nameRow}>
            <Text style={styles.name} numberOfLines={1}>👤 {user.fullName}</Text>
            <Badge label={roleLabel(user.role).toUpperCase()} variant={seller ? 'success' : 'primary'} size="sm" />
          </View>
          <Text style={styles.meta}>@{user.username} · {user.email}</Text>
          {user.phoneNumber ? <Text style={styles.meta}>{user.phoneNumber}</Text> : null}
        </View>

        <Text style={styles.sectionTitle}>MY ACCOUNT</Text>
        <View style={styles.group}>
          <Row icon="📍" label="Saved locations & addresses" hint="Where to search, and where orders are delivered" onPress={() => navigation.navigate('Locations')} />
          <Row icon="🚗" label="My Garage" hint="Your vehicles, to find parts that fit" onPress={() => navigation.navigate('Garage')} />
        </View>

        {seller ? (
          <>
            <Text style={styles.sectionTitle}>SELLING</Text>
            <View style={styles.group}>
              <Row icon="➕" label="Sell an item" hint="Auction or direct sale" onPress={() => navigation.navigate('MainTabs', { screen: 'Sell' })} />
              <Row icon="📦" label="My Listings" hint="Drafts, live, sold and ended" onPress={() => navigation.navigate('MyListings')} />
              <Row icon="🔎" label="Buyer requests" hint="Parts buyers are looking for" onPress={() => navigation.navigate('BuyerRequests')} />
              <Row
                icon="⭐"
                label="My seller profile & reviews"
                hint="What buyers see about you"
                onPress={() => navigation.navigate('SellerProfile', { seller: user.username, sellerName: user.fullName })}
              />
            </View>
          </>
        ) : (
          <View style={styles.sellCard}>
            <Text style={styles.sellTitle}>Want to sell on {config.APP_NAME}?</Text>
            {pending ? (
              <Text style={styles.sellText}>
                ✅ Request sent on {sentOn}. Our team is reviewing it and will contact you – the Sell tab appears when your seller account is ready.
              </Text>
            ) : application?.status === 'Approved' ? (
              <Text style={styles.sellText}>✅ Approved – your seller account is being set up. The Sell tab appears the next time the app refreshes.</Text>
            ) : (
              <>
                {application?.status === 'Rejected' ? (
                  <Text style={styles.sellNote}>
                    Your request from {sentOn} wasn't approved{application.adminNote ? `: ${application.adminNote}` : '.'} You can apply again.
                  </Text>
                ) : null}
                <Text style={styles.sellText}>Tell us about you and your business. Our team reviews it and sets up your seller account.</Text>
                <TouchableOpacity style={styles.sellBtn} onPress={() => navigation.navigate('SellerApplication')} activeOpacity={0.8}>
                  <Text style={styles.sellBtnText}>APPLY TO SELL</Text>
                </TouchableOpacity>
              </>
            )}
          </View>
        )}

        <TouchableOpacity style={styles.logout} onPress={confirmLogout} activeOpacity={0.8}>
          <Text style={styles.logoutText}>Log out</Text>
        </TouchableOpacity>
      </ScrollView>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  scroll: { padding: SPACING.md, paddingBottom: SPACING.xxl },
  card: { backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, padding: SPACING.md, borderWidth: 1, borderColor: COLORS.border, gap: 4 },
  nameRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: SPACING.sm },
  name: { color: COLORS.text, fontSize: 17, fontWeight: '800', flexShrink: 1 },
  meta: { color: COLORS.textSecondary, fontSize: 13 },
  sectionTitle: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1, marginTop: SPACING.lg, marginBottom: SPACING.sm },
  group: { backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, borderWidth: 1, borderColor: COLORS.border, overflow: 'hidden' },
  row: { flexDirection: 'row', alignItems: 'center', padding: SPACING.md, borderBottomWidth: 1, borderBottomColor: COLORS.border, gap: SPACING.sm },
  rowIcon: { fontSize: 18 },
  rowBody: { flex: 1 },
  rowLabel: { color: COLORS.text, fontSize: 14, fontWeight: '700' },
  rowHint: { color: COLORS.textMuted, fontSize: 12, marginTop: 2 },
  chevron: { color: COLORS.textMuted, fontSize: 22, fontWeight: '700' },
  sellCard: {
    backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, padding: SPACING.lg, borderWidth: 1, borderColor: COLORS.borderHighlight,
    marginTop: SPACING.lg, alignItems: 'center',
  },
  sellTitle: { color: COLORS.text, fontSize: 16, fontWeight: '800', textAlign: 'center' },
  sellText: { color: COLORS.textMuted, fontSize: 13, textAlign: 'center', marginTop: SPACING.xs, marginBottom: SPACING.md, lineHeight: 18 },
  sellNote: { color: COLORS.warning, fontSize: 13, textAlign: 'center', marginTop: SPACING.sm },
  sellBtn: {
    backgroundColor: COLORS.cardSecondary, paddingVertical: SPACING.md, borderRadius: RADIUS.md, borderWidth: 1, borderColor: COLORS.primary,
    width: '100%', alignItems: 'center',
  },
  sellBtnText: { color: COLORS.primaryGlow, fontSize: 13, fontWeight: '900', letterSpacing: 1 },
  logout: { marginTop: SPACING.xl, paddingVertical: SPACING.md, borderRadius: RADIUS.md, borderWidth: 1, borderColor: COLORS.danger, alignItems: 'center' },
  logoutText: { color: COLORS.danger, fontSize: 14, fontWeight: '800' },
});
