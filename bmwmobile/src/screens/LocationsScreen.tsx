import React, { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, Alert, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { Badge } from '../components/Badge';
import { Notice } from '../components/Notice';
import { useLocations } from '../location/LocationContext';
import { errorMessage } from '../services/authApi';
import type { SavedLocation } from '../types/location';
import { countryName, formatRadius, locationText } from '../utils/geo';
import { GpsError } from '../utils/gps';

type Props = NativeStackScreenProps<RootStackParamList, 'Locations'>;

export const addressIcon = (label: string) => (/^home$/i.test(label) ? '🏠' : /^work$/i.test(label) ? '💼' : '📍');

/** "Flat 4, Road 2 · Kukatpally · Hyderabad 500072, India" */
export const addressLines = (a: SavedLocation) =>
  [a.addressLine, a.area, [a.placeName, a.postalCode].filter(Boolean).join(' '), countryName(a.countryCode)].filter(Boolean).join(', ');

/**
 * My locations (opened from the location line on Home): use the phone's current location, or choose / add / edit
 * a saved address. The chosen one is where the app looks for parts (60 km / 60 mi) and the default item location.
 */
export const LocationsScreen: React.FC<Props> = ({ navigation }) => {
  const { location, usingCurrentLocation, addresses, switchToCurrentLocation, chooseAddress, removeAddress } = useLocations();
  const [busy, setBusy] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const run = async (key: string, action: () => Promise<void>, done?: () => void) => {
    setBusy(key);
    setError(null);
    setNotice(null);
    try {
      await action();
      done?.();
    } catch (err) {
      if (err instanceof GpsError) setNotice(err.message);
      else setError(errorMessage(err, 'Something went wrong. Please try again.'));
    } finally {
      setBusy(null);
    }
  };

  const confirmDelete = (a: SavedLocation) =>
    Alert.alert('Delete address', `Delete "${a.label}" (${addressLines(a)})?`, [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Delete', style: 'destructive', onPress: () => run(`del-${a.id}`, () => removeAddress(a.id)) },
    ]);

  const current = usingCurrentLocation ? locationText(location) : null;

  return (
    <SafeAreaView style={styles.container}>
      <Header title="My locations" subtitle="Where should we look for parts?" onBack={() => navigation.goBack()} />

      <ScrollView contentContainerStyle={styles.scroll}>
        {/* GPS */}
        <TouchableOpacity
          style={[styles.card, usingCurrentLocation && styles.cardActive]}
          onPress={() => run('gps', switchToCurrentLocation, () => navigation.goBack())}
          disabled={busy !== null}
          accessibilityRole="button"
          accessibilityState={{ selected: usingCurrentLocation }}
        >
          <View style={styles.cardTop}>
            <Text style={styles.icon}>🎯</Text>
            <View style={styles.cardText}>
              <View style={styles.labelRow}>
                <Text style={styles.label}>Use my current location</Text>
                {usingCurrentLocation ? <Badge label="IN USE" variant="success" size="sm" /> : null}
              </View>
              <Text style={styles.lines}>{current ?? 'Follows your phone (GPS) as you move'}</Text>
            </View>
            {busy === 'gps' ? <ActivityIndicator color={COLORS.primaryGlow} /> : null}
          </View>
        </TouchableOpacity>

        {notice ? <Notice tone="info" message={notice} /> : null}
        {error ? <Notice tone="error" message={error} /> : null}

        <Text style={styles.sectionHeader}>SAVED ADDRESSES ({addresses.length})</Text>
        {addresses.length === 0 ? (
          <Text style={styles.empty}>Add your home or workshop address to look for parts around it, wherever you are.</Text>
        ) : null}

        {addresses.map(a => (
          <View key={a.id} style={[styles.card, a.isPrimary && styles.cardActive]}>
            <TouchableOpacity
              style={styles.cardTop}
              onPress={() => (a.isPrimary ? navigation.goBack() : run(`use-${a.id}`, () => chooseAddress(a.id), () => navigation.goBack()))}
              disabled={busy !== null}
              accessibilityRole="button"
              accessibilityState={{ selected: a.isPrimary }}
            >
              <Text style={styles.icon}>{addressIcon(a.label)}</Text>
              <View style={styles.cardText}>
                <View style={styles.labelRow}>
                  <Text style={styles.label} numberOfLines={1}>{a.label}</Text>
                  {a.isPrimary ? <Badge label="IN USE" variant="success" size="sm" /> : null}
                </View>
                <Text style={styles.lines}>{addressLines(a)}</Text>
                {a.landmark ? <Text style={styles.small}>Near {a.landmark}</Text> : null}
                <Text style={styles.small}>Parts within {formatRadius(location?.addressId === a.id ? location.radiusKm : a.radiusKm, a.countryCode)}</Text>
              </View>
              {busy === `use-${a.id}` ? <ActivityIndicator color={COLORS.primaryGlow} /> : null}
            </TouchableOpacity>

            <View style={styles.actions}>
              {!a.isPrimary ? (
                <TouchableOpacity onPress={() => run(`use-${a.id}`, () => chooseAddress(a.id), () => navigation.goBack())} disabled={busy !== null}>
                  <Text style={styles.actionPrimary}>Use this address</Text>
                </TouchableOpacity>
              ) : null}
              <TouchableOpacity onPress={() => navigation.navigate('AddressForm', { id: a.id })} disabled={busy !== null}>
                <Text style={styles.action}>Edit</Text>
              </TouchableOpacity>
              {!a.isPrimary ? (
                <TouchableOpacity onPress={() => confirmDelete(a)} disabled={busy !== null}>
                  <Text style={styles.actionDanger}>Delete</Text>
                </TouchableOpacity>
              ) : null}
            </View>
          </View>
        ))}

        <TouchableOpacity style={styles.addBtn} onPress={() => navigation.navigate('AddressForm', {})} disabled={busy !== null}>
          <Text style={styles.addText}>+ ADD NEW ADDRESS</Text>
        </TouchableOpacity>
      </ScrollView>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  scroll: { padding: SPACING.md, paddingBottom: SPACING.xxl, gap: SPACING.sm },
  card: { backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, padding: SPACING.md, borderWidth: 1, borderColor: COLORS.border },
  cardActive: { borderColor: COLORS.successGlow },
  cardTop: { flexDirection: 'row', alignItems: 'flex-start', gap: SPACING.md },
  icon: { fontSize: 22 },
  cardText: { flex: 1, gap: 3 },
  labelRow: { flexDirection: 'row', alignItems: 'center', gap: SPACING.sm },
  label: { color: COLORS.text, fontSize: 15, fontWeight: '800', flexShrink: 1 },
  lines: { color: COLORS.textSecondary, fontSize: 13, lineHeight: 18 },
  small: { color: COLORS.textMuted, fontSize: 11 },
  actions: { flexDirection: 'row', gap: SPACING.lg, marginTop: SPACING.sm, marginLeft: 38 },
  actionPrimary: { color: COLORS.primaryGlow, fontSize: 13, fontWeight: '800' },
  action: { color: COLORS.textSecondary, fontSize: 13, fontWeight: '700' },
  actionDanger: { color: COLORS.danger, fontSize: 13, fontWeight: '700' },
  sectionHeader: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1, marginTop: SPACING.md },
  empty: { color: COLORS.textMuted, fontSize: 13, lineHeight: 18 },
  addBtn: { backgroundColor: COLORS.primary, paddingVertical: 14, borderRadius: RADIUS.md, alignItems: 'center', marginTop: SPACING.sm },
  addText: { color: COLORS.text, fontSize: 14, fontWeight: '900', letterSpacing: 0.5 },
});
