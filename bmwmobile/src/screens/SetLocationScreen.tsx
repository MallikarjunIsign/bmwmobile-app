import React, { useEffect, useMemo, useRef, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, ActivityIndicator, KeyboardAvoidingView, Platform } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { Chip } from '../components/Chip';
import { FormInput } from '../components/FormInput';
import { CheckboxRow } from '../components/CheckboxRow';
import { Notice } from '../components/Notice';
import { PrimaryButton } from '../components/PrimaryButton';
import { useAuth } from '../auth/AuthContext';
import { useLocations } from '../location/LocationContext';
import { LocationApi } from '../services/locationApi';
import { errorMessage } from '../services/authApi';
import type { City, CountryCode, PickedPlace } from '../types/location';
import { countryFromPhone, DEFAULT_RADIUS_KM, LABEL_OPTIONS, RADIUS_OPTIONS_KM, radiusSummary } from '../utils/geo';
import { getCurrentPosition, GpsError } from '../utils/gps';

type Props = NativeStackScreenProps<RootStackParamList, 'SetLocation'>;

const SEARCH_DEBOUNCE_MS = 300;

/**
 * One screen for three jobs:
 *  - onboarding: "Set your location" once after the first login (no back button);
 *  - add: a new saved place (Home, Work, ...);
 *  - edit: change a saved place (for the GPS row only the radius).
 */
export const SetLocationScreen: React.FC<Props> = ({ navigation, route }) => {
  const mode = route.params?.mode ?? 'onboarding';
  const { user } = useAuth();
  const { locations, add, update, useCurrentLocation } = useLocations();
  const editId = route.params?.mode === 'edit' ? route.params.locationId : null;
  const editing = editId ? locations.find(l => l.id === editId) ?? null : null;
  const gpsRow = !!editing?.isCurrentLocation;

  const initialLabel = editing?.label ?? (mode === 'add' ? 'Home' : 'Home');
  const [label, setLabel] = useState(LABEL_OPTIONS.includes(initialLabel as any) ? initialLabel : 'Custom');
  const [customLabel, setCustomLabel] = useState(LABEL_OPTIONS.includes(initialLabel as any) ? '' : initialLabel);
  const [place, setPlace] = useState<PickedPlace | null>(
    editing
      ? { name: editing.placeName, latitude: editing.latitude, longitude: editing.longitude, countryCode: editing.countryCode, fromGps: editing.isCurrentLocation }
      : null,
  );
  const [radiusKm, setRadiusKm] = useState<number>(editing?.radiusKm ?? DEFAULT_RADIUS_KM);
  const [makePrimary, setMakePrimary] = useState(false);

  // Popular chips follow GPS / phone country: India, USA or Nigeria.
  const [country, setCountry] = useState<CountryCode>(editing?.countryCode ?? countryFromPhone(user?.phoneNumber));
  const [popular, setPopular] = useState<City[]>([]);
  const [popularLoading, setPopularLoading] = useState(true);
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<City[]>([]);
  const [searching, setSearching] = useState(false);

  const [gpsBusy, setGpsBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const savingRef = useRef(false);

  useEffect(() => {
    let alive = true;
    setPopularLoading(true);
    LocationApi.popularCities(country)
      .then(res => alive && setPopular(res.cities))
      .catch(() => alive && setPopular([]))
      .finally(() => alive && setPopularLoading(false));
    return () => {
      alive = false;
    };
  }, [country]);

  // City search (India, USA, Nigeria), debounced; late answers are ignored.
  useEffect(() => {
    const q = query.trim();
    if (!q) {
      setResults([]);
      setSearching(false);
      return;
    }
    let alive = true;
    setSearching(true);
    const timer = setTimeout(() => {
      LocationApi.searchCities(q, country)
        .then(list => alive && setResults(list))
        .catch(() => alive && setResults([]))
        .finally(() => alive && setSearching(false));
    }, SEARCH_DEBOUNCE_MS);
    return () => {
      alive = false;
      clearTimeout(timer);
    };
  }, [query, country]);

  const pickCity = (city: City) => {
    setPlace({ name: city.name, latitude: city.latitude, longitude: city.longitude, countryCode: city.countryCode, fromGps: false });
    setError(null);
    setNotice(null);
  };

  const handleUseGps = async () => {
    setError(null);
    setNotice(null);
    setGpsBusy(true);
    try {
      const pos = await getCurrentPosition({ prompt: true });
      const nearest = await LocationApi.nearestCity(pos.latitude, pos.longitude);
      setPlace({ name: nearest.name, latitude: pos.latitude, longitude: pos.longitude, countryCode: nearest.countryCode, fromGps: true });
      if (nearest.countryCode) setCountry(nearest.countryCode);
    } catch (err) {
      setNotice(err instanceof GpsError ? err.message : errorMessage(err, "We couldn't find your location. Please pick a city."));
    } finally {
      setGpsBusy(false);
    }
  };

  const finalLabel = label === 'Custom' ? customLabel.trim() || 'Other' : label;

  const handleSave = async () => {
    if (!place || savingRef.current) return;
    savingRef.current = true;
    setSaving(true);
    setError(null);
    try {
      const body = {
        label: finalLabel,
        placeName: place.name,
        latitude: place.latitude,
        longitude: place.longitude,
        radiusKm,
        countryCode: place.countryCode,
      };
      if (mode === 'onboarding') {
        // GPS → primary follows the phone (auto-update on). A city → a fixed "Home" primary.
        if (place.fromGps) await useCurrentLocation(radiusKm);
        else await add({ ...body, label: 'Home', makePrimary: true });
        // The navigator moves on to the marketplace by itself.
      } else if (mode === 'add') {
        await add({ ...body, makePrimary });
        navigation.goBack();
      } else if (editing) {
        await update(editing.id, body);
        navigation.goBack();
      }
    } catch (err) {
      setError(errorMessage(err, "Couldn't save the location. Please try again."));
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  const title = mode === 'onboarding' ? 'Set Your Location' : mode === 'add' ? 'Add Location' : 'Edit Location';
  const subtitle = mode === 'onboarding' ? "We'll show BMW parts near you" : 'Saved places never move by themselves';
  const popularTitle = useMemo(
    () => `POPULAR CITIES · ${country === 'IN' ? 'INDIA' : country === 'US' ? 'USA' : 'NIGERIA'}`,
    [country],
  );

  return (
    <SafeAreaView style={styles.container}>
      <Header title={title} subtitle={subtitle} onBack={mode === 'onboarding' ? undefined : () => navigation.goBack()} />

      <KeyboardAvoidingView style={styles.keyboardView} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
          {mode !== 'onboarding' && !gpsRow ? (
            <View style={styles.section}>
              <Text style={styles.sectionHeader}>LABEL</Text>
              <View style={styles.chips}>
                {[...LABEL_OPTIONS, 'Custom'].map(l => (
                  <Chip key={l} label={l} selected={label === l} onPress={() => setLabel(l)} />
                ))}
              </View>
              {label === 'Custom' ? (
                <FormInput label="Custom label" value={customLabel} onChangeText={setCustomLabel} placeholder="e.g. Garage, Mum's place" maxLength={40} />
              ) : null}
            </View>
          ) : null}

          {gpsRow ? (
            <Notice tone="info" message={`This place follows your GPS (${editing?.placeName}). You can change its radius.`} />
          ) : (
            <>
              <View style={styles.section}>
                <PrimaryButton
                  variant="outline"
                  label="Use my current location"
                  icon={<Text style={styles.btnIcon}>📍</Text>}
                  onPress={handleUseGps}
                  loading={gpsBusy}
                  disabled={saving}
                />
                {notice ? <Notice tone="info" message={notice} /> : null}
                {place ? (
                  <View style={styles.selected}>
                    <Text style={styles.selectedIcon}>{place.fromGps ? '📡' : '📍'}</Text>
                    <View style={styles.selectedCol}>
                      <Text style={styles.selectedName} numberOfLines={1}>{place.name}</Text>
                      <Text style={styles.selectedSub}>
                        {place.fromGps ? 'Your current location' : 'Selected city'}
                      </Text>
                    </View>
                    <Text style={styles.tick}>✓</Text>
                  </View>
                ) : null}
              </View>

              <View style={styles.section}>
                <Text style={styles.sectionHeader}>{popularTitle}</Text>
                {popularLoading ? (
                  <ActivityIndicator color={COLORS.primaryGlow} style={styles.spinner} />
                ) : (
                  <View style={styles.chips}>
                    {popular.map(city => (
                      <Chip key={city.name} label={city.name} selected={!place?.fromGps && place?.name === city.name} onPress={() => pickCity(city)} />
                    ))}
                  </View>
                )}
                <View style={styles.countryRow}>
                  {(['IN', 'US', 'NG'] as CountryCode[]).map(c => (
                    <TouchableOpacity key={c} onPress={() => setCountry(c)} hitSlop={6}>
                      <Text style={[styles.countryLink, country === c && styles.countryLinkActive]}>
                        {c === 'IN' ? '🇮🇳 India' : c === 'US' ? '🇺🇸 USA' : '🇳🇬 Nigeria'}
                      </Text>
                    </TouchableOpacity>
                  ))}
                </View>
              </View>

              <View style={styles.section}>
                <FormInput
                  label="Search City"
                  value={query}
                  onChangeText={setQuery}
                  placeholder="Type a city name"
                  autoCapitalize="words"
                  autoCorrect={false}
                  returnKeyType="search"
                />
                {searching ? <ActivityIndicator color={COLORS.primaryGlow} style={styles.spinner} /> : null}
                {!searching && query.trim() && results.length === 0 ? (
                  <Text style={styles.empty}>No cities found. Try another name, or use your current location.</Text>
                ) : null}
                {!searching && results.length > 0 ? (
                  <View style={styles.results}>
                    {results.map((city, i) => {
                      const active = !place?.fromGps && place?.name === city.name;
                      return (
                        <TouchableOpacity
                          key={`${city.countryCode}-${city.name}`}
                          style={[styles.resultRow, i > 0 && styles.resultDivider, active && styles.resultActive]}
                          onPress={() => pickCity(city)}
                          activeOpacity={0.7}
                        >
                          <Text style={styles.resultText}>{city.name}</Text>
                          <Text style={styles.resultCountry}>{city.countryCode}</Text>
                        </TouchableOpacity>
                      );
                    })}
                  </View>
                ) : null}
              </View>
            </>
          )}

          <View style={styles.section}>
            <Text style={styles.sectionHeader}>SEARCH RADIUS</Text>
            <View style={styles.chips}>
              {RADIUS_OPTIONS_KM.map(km => (
                <Chip key={km} label={`${km} km`} selected={radiusKm === km} onPress={() => setRadiusKm(km)} />
              ))}
            </View>
            <Text style={styles.summary}>{radiusSummary(radiusKm, place?.name)}</Text>
          </View>

          {mode === 'add' ? (
            <View style={styles.section}>
              <CheckboxRow checked={makePrimary} onToggle={() => setMakePrimary(v => !v)} label="Make this my primary location" />
            </View>
          ) : null}

          {error ? <Notice tone="error" message={error} /> : null}

          <PrimaryButton
            label={mode === 'onboarding' ? 'SAVE & CONTINUE' : mode === 'add' ? 'SAVE LOCATION' : 'SAVE CHANGES'}
            onPress={handleSave}
            loading={saving}
            disabled={!place || gpsBusy}
            style={styles.saveBtn}
          />
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.background,
  },
  keyboardView: {
    flex: 1,
  },
  scroll: {
    padding: SPACING.md,
    paddingBottom: SPACING.xxl,
  },
  section: {
    marginTop: SPACING.lg,
    gap: SPACING.sm,
  },
  sectionHeader: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1,
  },
  chips: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: SPACING.sm,
  },
  btnIcon: {
    fontSize: 16,
  },
  selected: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: SPACING.md,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.primaryGlow,
    backgroundColor: COLORS.badgeBg,
  },
  selectedIcon: {
    fontSize: 20,
  },
  selectedCol: {
    flex: 1,
  },
  selectedName: {
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '800',
  },
  selectedSub: {
    color: COLORS.textMuted,
    fontSize: 12,
    marginTop: 2,
  },
  tick: {
    color: COLORS.successGlow,
    fontSize: 18,
    fontWeight: '900',
  },
  spinner: {
    alignSelf: 'flex-start',
  },
  countryRow: {
    flexDirection: 'row',
    gap: SPACING.md,
    marginTop: SPACING.xs,
  },
  countryLink: {
    color: COLORS.textMuted,
    fontSize: 12,
    fontWeight: '700',
  },
  countryLinkActive: {
    color: COLORS.primaryGlow,
  },
  empty: {
    color: COLORS.textMuted,
    fontSize: 13,
  },
  results: {
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.border,
    backgroundColor: COLORS.card,
    overflow: 'hidden',
  },
  resultRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: SPACING.md,
    paddingVertical: 14,
  },
  resultDivider: {
    borderTopWidth: 1,
    borderTopColor: COLORS.border,
  },
  resultActive: {
    backgroundColor: COLORS.badgeBg,
  },
  resultText: {
    flex: 1,
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '600',
  },
  resultCountry: {
    color: COLORS.textMuted,
    fontSize: 12,
    fontWeight: '800',
  },
  summary: {
    color: COLORS.textMuted,
    fontSize: 12,
  },
  saveBtn: {
    marginTop: SPACING.xl,
  },
});
