import React, { useEffect, useRef, useState } from 'react';
import { View, Text, TextInput, TouchableOpacity, ActivityIndicator, StyleSheet, Keyboard } from 'react-native';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { MIN_PLACE_QUERY, newSessionToken, PLACE_DEBOUNCE_MS, PlacesApi } from '../services/placesApi';
import { errorMessage } from '../services/authApi';
import type { PlaceDetails, PlaceSuggestion } from '../types/location';
import { countryName, isCountryCode } from '../utils/geo';
import { getCurrentPosition, GpsError } from '../utils/gps';

export const UNSUPPORTED_COUNTRY = "We don't operate in this country yet";

/** "Kukatpally, Hyderabad, Telangana 500072, India" – the line under the chosen place. */
const placeLine = (p: PlaceDetails) =>
  p.formattedAddress && p.formattedAddress !== p.label
    ? p.formattedAddress
    : [p.label, isCountryCode(p.countryCode) ? countryName(p.countryCode) : p.countryCode].filter(Boolean).join(', ');

interface Props {
  /** The chosen place (shown above the search box with its full address). */
  value: PlaceDetails | null;
  /** A place was picked (a suggestion or "Use my current location"); only places in our countries. */
  onPick: (place: PlaceDetails) => void;
  /** Results are biased toward this point (the user's location). */
  near?: { latitude: number; longitude: number } | null;
  label?: string;
  placeholder?: string;
  /** "🎯 Use my current location" at the top (default on). */
  showCurrentLocation?: boolean;
  error?: string | null;
  testID?: string;
}

/**
 * The one "Search location" box used wherever a location is chosen: type any area, street, landmark or address
 * (suggestions after 3 characters and a 300 ms pause), or use the phone's position. Google results carry the
 * required "powered by Google" line. When Google can't be used the server (or the app) offers the city list.
 * Put it inside a ScrollView with keyboardShouldPersistTaps="handled" so taps on suggestions aren't swallowed.
 */
export const PlaceSearch: React.FC<Props> = ({
  value, onPick, near, label = 'Location', placeholder = 'Search area, street, landmark or address',
  showCurrentLocation = true, error, testID = 'place-search',
}) => {
  const [query, setQuery] = useState('');
  const [suggestions, setSuggestions] = useState<PlaceSuggestion[]>([]);
  const [source, setSource] = useState<'google' | 'cities' | null>(null);
  const [loading, setLoading] = useState(false);
  const [searched, setSearched] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  // One session token per search (typing → picking); a new one after a pick.
  const session = useRef<string | null>(null);
  const request = useRef(0);

  const text = query.trim();
  const active = text.length >= MIN_PLACE_QUERY;

  useEffect(() => {
    const id = ++request.current;
    if (!active) {
      setSuggestions([]);
      setSource(null);
      setLoading(false);
      setSearched(false);
      return;
    }
    setLoading(true);
    const timer = setTimeout(() => {
      session.current ??= newSessionToken();
      PlacesApi.autocomplete(text, session.current, near)
        .then(r => {
          if (id !== request.current) return;
          setSuggestions(r.suggestions);
          setSource(r.source);
          setNotice(null);
        })
        .catch(e => id === request.current && setNotice(errorMessage(e, "Couldn't search for places. Please try again.")))
        .finally(() => {
          if (id !== request.current) return;
          setLoading(false);
          setSearched(true);
        });
    }, PLACE_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [text]); // eslint-disable-line react-hooks/exhaustive-deps

  const accept = (place: PlaceDetails) => {
    if (!place.supported) {
      setNotice(place.message || UNSUPPORTED_COUNTRY);
      return;
    }
    request.current++;
    session.current = null;
    setQuery('');
    setSuggestions([]);
    setSearched(false);
    setNotice(null);
    Keyboard.dismiss();
    onPick(place);
  };

  const pick = async (s: PlaceSuggestion) => {
    setBusy(s.placeId);
    setNotice(null);
    try {
      accept(await PlacesApi.details(s.placeId, session.current));
    } catch (e) {
      setNotice(errorMessage(e, "Couldn't open this place. Please try again."));
    } finally {
      setBusy(null);
    }
  };

  const useCurrentLocation = async () => {
    setBusy('gps');
    setNotice(null);
    try {
      const pos = await getCurrentPosition({ prompt: true });
      // Named by the server (Google, else the nearest city); offline: just the point.
      const place = await PlacesApi.reverse(pos.latitude, pos.longitude).catch(
        (): PlaceDetails => ({ placeId: null, formattedAddress: '', lat: pos.latitude, lon: pos.longitude, label: 'Current location', supported: true, source: 'local' }),
      );
      accept({ ...place, lat: pos.latitude, lon: pos.longitude });
    } catch (e) {
      setNotice(e instanceof GpsError ? e.message : errorMessage(e, "Couldn't read your position. Search for the place instead."));
    } finally {
      setBusy(null);
    }
  };

  return (
    <View style={styles.wrap} testID={testID}>
      <Text style={styles.label}>{label.toUpperCase()}</Text>
      {value ? (
        <View style={styles.chosen} testID={`${testID}-value`}>
          <Text style={styles.chosenTitle} numberOfLines={1}>📍 {value.label}</Text>
          <Text style={styles.chosenLine} numberOfLines={2}>{placeLine(value)}</Text>
        </View>
      ) : null}

      <TextInput
        style={styles.input}
        value={query}
        onChangeText={setQuery}
        placeholder={value ? 'Search another place' : placeholder}
        placeholderTextColor={COLORS.textMuted}
        autoCorrect={false}
        returnKeyType="search"
        accessibilityLabel="Search location"
        testID={`${testID}-input`}
      />

      {showCurrentLocation ? (
        <TouchableOpacity style={styles.row} onPress={useCurrentLocation} disabled={busy !== null} accessibilityRole="button"
          testID={`${testID}-gps`}>
          <Text style={styles.gpsText}>🎯 Use my current location</Text>
          {busy === 'gps' ? <ActivityIndicator color={COLORS.primaryGlow} /> : null}
        </TouchableOpacity>
      ) : null}

      {active ? (
        <View style={styles.results}>
          {loading ? (
            <View style={styles.row}>
              <ActivityIndicator color={COLORS.primaryGlow} />
              <Text style={styles.muted}>Searching…</Text>
            </View>
          ) : null}
          {!loading && searched && suggestions.length === 0 && !notice ? <Text style={styles.empty}>No places found</Text> : null}
          {suggestions.map(s => (
            <TouchableOpacity key={s.placeId} style={styles.row} onPress={() => pick(s)} disabled={busy !== null}
              accessibilityRole="button" testID={`${testID}-suggestion`}>
              <View style={styles.rowText}>
                <Text style={styles.primary} numberOfLines={1}>{s.primaryText}</Text>
                {s.secondaryText ? <Text style={styles.secondary} numberOfLines={1}>{s.secondaryText}</Text> : null}
              </View>
              {busy === s.placeId ? <ActivityIndicator color={COLORS.primaryGlow} /> : null}
            </TouchableOpacity>
          ))}
          {/* Required by Google when its results are not shown on a Google map. */}
          {source === 'google' && suggestions.length > 0 ? <Text style={styles.attribution}>powered by Google</Text> : null}
        </View>
      ) : null}

      {notice ? <Text style={styles.notice}>{notice}</Text> : null}
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </View>
  );
};

const styles = StyleSheet.create({
  wrap: { marginBottom: SPACING.md },
  label: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1, marginBottom: 4 },
  chosen: {
    backgroundColor: COLORS.card, borderRadius: RADIUS.md, borderWidth: 1, borderColor: COLORS.primaryGlow,
    padding: SPACING.sm, marginBottom: SPACING.sm, gap: 2,
  },
  chosenTitle: { color: COLORS.text, fontSize: 15, fontWeight: '800' },
  chosenLine: { color: COLORS.textSecondary, fontSize: 12 },
  input: {
    backgroundColor: COLORS.surface, borderRadius: RADIUS.md, color: COLORS.text, fontSize: 15, paddingHorizontal: SPACING.md,
    minHeight: 44, borderWidth: 1, borderColor: COLORS.border,
  },
  results: { borderWidth: 1, borderColor: COLORS.border, borderRadius: RADIUS.md, marginTop: SPACING.xs, paddingHorizontal: SPACING.sm },
  row: {
    flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: SPACING.sm,
    paddingVertical: SPACING.sm + 2, borderBottomWidth: 1, borderBottomColor: COLORS.border,
  },
  rowText: { flex: 1, gap: 2 },
  primary: { color: COLORS.text, fontSize: 14, fontWeight: '800' },
  secondary: { color: COLORS.textMuted, fontSize: 12 },
  gpsText: { color: COLORS.primaryGlow, fontSize: 14, fontWeight: '800' },
  muted: { color: COLORS.textMuted, fontSize: 13, flex: 1 },
  empty: { color: COLORS.textMuted, fontSize: 13, paddingVertical: SPACING.sm },
  attribution: { color: COLORS.textMuted, fontSize: 11, textAlign: 'right', paddingVertical: SPACING.xs },
  notice: { color: COLORS.warningGlow, fontSize: 12, fontWeight: '600', marginTop: SPACING.xs },
  error: { color: COLORS.danger, fontSize: 12, marginTop: SPACING.xs },
});
