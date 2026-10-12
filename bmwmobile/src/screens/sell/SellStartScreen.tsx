import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, Image, Alert, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect } from '@react-navigation/native';
import type { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import type { CompositeScreenProps } from '@react-navigation/native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import type { MainTabParamList, RootStackParamList } from '../../types/navigation';
import { COLORS, SPACING, RADIUS } from '../../theme/colors';
import { MarketApi } from '../../services/marketApi';
import { ServerError } from '../../services/api';
import { useLocations } from '../../location/LocationContext';
import type { ListingKind, MyListing } from '../../types/listing';

type Props = CompositeScreenProps<BottomTabScreenProps<MainTabParamList, 'Sell'>, NativeStackScreenProps<RootStackParamList>>;

const KINDS: { kind: ListingKind; icon: string; title: string; text: string }[] = [
  {
    kind: 'CommonPart',
    icon: '🧰',
    title: 'Common part',
    text: 'Fits any vehicle – universal (dash cam, tool kit, car cover) or by size (tyre, bulb, battery, wiper blade, oil).',
  },
  {
    kind: 'VehicleSpecificPart',
    icon: '🚘',
    title: 'Vehicle-specific part',
    text: 'Fits certain vehicles – bumper, headlamp, gearbox, door … You say which years, makes and models.',
  },
  { kind: 'Vehicle', icon: '🚗', title: 'Vehicle', text: 'A whole car, bike or truck.' },
];

/** "+ Sell": "What are you adding?" – and the drafts you can continue. */
export const SellStartScreen: React.FC<Props> = ({ navigation }) => {
  const { location, country } = useLocations();
  const [drafts, setDrafts] = useState<MyListing[]>([]);
  const [starting, setStarting] = useState<ListingKind | null>(null);

  useFocusEffect(
    useCallback(() => {
      MarketApi.myListings().then(list => setDrafts(list.filter(l => l.state === 'Draft'))).catch(() => undefined);
    }, []),
  );

  const start = async (kind: ListingKind) => {
    setStarting(kind);
    try {
      // The item location defaults to the seller's automatic location.
      const draft = await MarketApi.createListing(kind, {
        countryCode: location?.countryCode ?? country,
        locationName: location?.city ?? undefined,
        latitude: location?.latitude,
        longitude: location?.longitude,
      });
      navigation.navigate('SellWizard', { listingId: draft.id });
    } catch (e) {
      Alert.alert('Could not start', e instanceof ServerError ? e.message : 'Please try again.');
    } finally {
      setStarting(null);
    }
  };

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.title}>Sell on ShopCarParts</Text>
        <TouchableOpacity onPress={() => navigation.navigate('MyListings')}>
          <Text style={styles.link}>My listings →</Text>
        </TouchableOpacity>
      </View>
      <ScrollView contentContainerStyle={styles.scroll}>
        <Text style={styles.question}>What are you adding?</Text>
        {KINDS.map(k => (
          <TouchableOpacity key={k.kind} style={styles.option} onPress={() => start(k.kind)} disabled={starting !== null} activeOpacity={0.85}>
            <Text style={styles.icon}>{k.icon}</Text>
            <View style={styles.optionText}>
              <Text style={styles.optionTitle}>{k.title}</Text>
              <Text style={styles.optionDesc}>{k.text}</Text>
            </View>
            {starting === k.kind ? <ActivityIndicator color={COLORS.primaryGlow} /> : <Text style={styles.chevron}>›</Text>}
          </TouchableOpacity>
        ))}

        {drafts.length > 0 ? (
          <>
            <Text style={styles.section}>CONTINUE A DRAFT</Text>
            {drafts.map(d => (
              <TouchableOpacity key={d.id} style={styles.draft} onPress={() => navigation.navigate('SellWizard', { listingId: d.id })}>
                {d.imageUrl ? <Image source={{ uri: d.imageUrl }} style={styles.thumb} /> : <View style={[styles.thumb, styles.noThumb]} />}
                <View style={styles.optionText}>
                  <Text style={styles.draftTitle} numberOfLines={2}>{d.title || 'Untitled draft'}</Text>
                  <Text style={styles.optionDesc}>Saved {new Date(d.updatedAt).toLocaleDateString()}</Text>
                </View>
                <Text style={styles.chevron}>›</Text>
              </TouchableOpacity>
            ))}
          </>
        ) : null}
      </ScrollView>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  header: {
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', padding: SPACING.md,
    backgroundColor: COLORS.surface, borderBottomWidth: 1, borderBottomColor: COLORS.border,
  },
  title: { color: COLORS.text, fontSize: 18, fontWeight: '800' },
  link: { color: COLORS.primaryGlow, fontSize: 13, fontWeight: '700' },
  scroll: { padding: SPACING.md, paddingBottom: SPACING.xxl },
  question: { color: COLORS.text, fontSize: 20, fontWeight: '900', marginBottom: SPACING.md },
  option: {
    flexDirection: 'row', alignItems: 'center', gap: SPACING.md, backgroundColor: COLORS.surface, borderRadius: RADIUS.lg,
    borderWidth: 1, borderColor: COLORS.borderHighlight, padding: SPACING.md, marginBottom: SPACING.md,
  },
  icon: { fontSize: 30 },
  optionText: { flex: 1 },
  optionTitle: { color: COLORS.text, fontSize: 16, fontWeight: '800' },
  optionDesc: { color: COLORS.textMuted, fontSize: 12, lineHeight: 17, marginTop: 2 },
  chevron: { color: COLORS.primaryGlow, fontSize: 24, fontWeight: '700' },
  section: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1, marginTop: SPACING.md, marginBottom: SPACING.sm },
  draft: {
    flexDirection: 'row', alignItems: 'center', gap: SPACING.md, backgroundColor: COLORS.surface, borderRadius: RADIUS.md,
    borderWidth: 1, borderColor: COLORS.border, padding: SPACING.sm, marginBottom: SPACING.sm,
  },
  thumb: { width: 56, height: 56, borderRadius: RADIUS.sm, backgroundColor: COLORS.card },
  noThumb: { borderWidth: 1, borderColor: COLORS.border },
  draftTitle: { color: COLORS.text, fontSize: 14, fontWeight: '700' },
});
