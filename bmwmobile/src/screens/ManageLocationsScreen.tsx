import React, { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, Switch, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { Badge } from '../components/Badge';
import { Notice } from '../components/Notice';
import { PrimaryButton } from '../components/PrimaryButton';
import { useLocations } from '../location/LocationContext';
import { errorMessage } from '../services/authApi';
import type { UserLocation } from '../types/location';
import { formatRadius, locationIcon } from '../utils/geo';
import { GpsError } from '../utils/gps';

type Props = NativeStackScreenProps<RootStackParamList, 'ManageLocations'>;

export const ManageLocationsScreen: React.FC<Props> = ({ navigation }) => {
  const { locations, autoUpdatePrimary, setPrimary, remove, setAutoUpdate, useCurrentLocation } = useLocations();
  const [busy, setBusy] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const run = async (key: string, action: () => Promise<void>) => {
    setBusy(key);
    setError(null);
    setNotice(null);
    try {
      await action();
    } catch (err) {
      if (err instanceof GpsError) setNotice(err.message);
      else setError(errorMessage(err, 'Something went wrong. Please try again.'));
    } finally {
      setBusy(null);
    }
  };

  // Turning auto-update on needs GPS: ask for permission and move the primary to the phone's position.
  const handleAutoToggle = (on: boolean) =>
    run('auto', async () => {
      if (on) await useCurrentLocation();
      else await setAutoUpdate(false);
    });

  const confirmDelete = (l: UserLocation) =>
    Alert.alert('Delete location', `Delete "${l.label}" (${l.placeName})?`, [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Delete', style: 'destructive', onPress: () => run(`del-${l.id}`, () => remove(l.id)) },
    ]);

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Manage Locations" subtitle="Where should we look for parts?" onBack={() => navigation.goBack()} />

      <ScrollView contentContainerStyle={styles.scroll}>
        <View style={styles.autoCard}>
          <View style={styles.autoText}>
            <Text style={styles.autoTitle}>Auto-update primary from my current location</Text>
            <Text style={styles.autoSub}>
              {autoUpdatePrimary
                ? 'Your primary location follows your phone when you move more than 1 km.'
                : 'Off — your primary location stays where you set it.'}
            </Text>
          </View>
          <Switch
            value={autoUpdatePrimary}
            onValueChange={handleAutoToggle}
            disabled={busy !== null}
            trackColor={{ false: COLORS.border, true: COLORS.primary }}
            thumbColor={COLORS.text}
          />
        </View>

        <PrimaryButton
          variant="outline"
          label="Use my current location"
          icon={<Text style={styles.btnIcon}>📍</Text>}
          onPress={() => run('gps', () => useCurrentLocation())}
          loading={busy === 'gps'}
          disabled={busy !== null && busy !== 'gps'}
          style={styles.gpsBtn}
        />

        {notice ? <Notice tone="info" message={notice} /> : null}
        {error ? <Notice tone="error" message={error} /> : null}

        <Text style={styles.sectionHeader}>SAVED LOCATIONS ({locations.length})</Text>

        {locations.map(l => (
          <View key={l.id} style={[styles.card, l.isPrimary && styles.cardPrimary]}>
            <View style={styles.cardTop}>
              <Text style={styles.icon}>{locationIcon(l)}</Text>
              <View style={styles.cardText}>
                <View style={styles.labelRow}>
                  <Text style={styles.label} numberOfLines={1}>{l.label}</Text>
                  {l.isPrimary ? <Badge label="PRIMARY" variant="primary" size="sm" /> : null}
                </View>
                <Text style={styles.place} numberOfLines={1}>
                  {l.placeName} · {formatRadius(l.radiusKm, l.countryCode)}
                </Text>
              </View>
            </View>

            <View style={styles.actions}>
              {!l.isPrimary ? (
                <TouchableOpacity onPress={() => run(`pri-${l.id}`, () => setPrimary(l.id))} disabled={busy !== null}>
                  <Text style={styles.actionPrimary}>{busy === `pri-${l.id}` ? 'Saving…' : '★ Set as primary'}</Text>
                </TouchableOpacity>
              ) : null}
              <TouchableOpacity onPress={() => navigation.navigate('SetLocation', { mode: 'edit', locationId: l.id })} disabled={busy !== null}>
                <Text style={styles.action}>Edit</Text>
              </TouchableOpacity>
              {!l.isPrimary ? (
                <TouchableOpacity onPress={() => confirmDelete(l)} disabled={busy !== null}>
                  <Text style={styles.actionDanger}>Delete</Text>
                </TouchableOpacity>
              ) : null}
            </View>
          </View>
        ))}

        <PrimaryButton
          label="+ ADD LOCATION"
          onPress={() => navigation.navigate('SetLocation', { mode: 'add' })}
          disabled={busy !== null}
          style={styles.addBtn}
        />
      </ScrollView>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.background,
  },
  scroll: {
    padding: SPACING.md,
    paddingBottom: SPACING.xxl,
  },
  autoCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: SPACING.md,
    backgroundColor: COLORS.card,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.border,
    padding: SPACING.md,
  },
  autoText: {
    flex: 1,
  },
  autoTitle: {
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '800',
  },
  autoSub: {
    color: COLORS.textMuted,
    fontSize: 12,
    marginTop: 4,
  },
  btnIcon: {
    fontSize: 16,
  },
  gpsBtn: {
    marginTop: SPACING.md,
  },
  sectionHeader: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1,
    marginTop: SPACING.lg,
    marginBottom: SPACING.sm,
  },
  card: {
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.border,
    padding: SPACING.md,
    marginBottom: SPACING.sm,
  },
  cardPrimary: {
    borderColor: COLORS.primaryGlow,
  },
  cardTop: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: SPACING.md,
  },
  icon: {
    fontSize: 22,
  },
  cardText: {
    flex: 1,
  },
  labelRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: SPACING.sm,
  },
  label: {
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '800',
    flexShrink: 1,
  },
  place: {
    color: COLORS.textSecondary,
    fontSize: 13,
    marginTop: 2,
  },
  actions: {
    flexDirection: 'row',
    justifyContent: 'flex-end',
    gap: SPACING.lg,
    marginTop: SPACING.sm,
    paddingTop: SPACING.sm,
    borderTopWidth: 1,
    borderTopColor: COLORS.border,
  },
  actionPrimary: {
    color: COLORS.primaryGlow,
    fontSize: 13,
    fontWeight: '800',
  },
  action: {
    color: COLORS.textSecondary,
    fontSize: 13,
    fontWeight: '700',
  },
  actionDanger: {
    color: COLORS.danger,
    fontSize: 13,
    fontWeight: '700',
  },
  addBtn: {
    marginTop: SPACING.md,
  },
});
