import React, { useState } from 'react';
import { Text, StyleSheet, ScrollView, TouchableOpacity, ActivityIndicator, KeyboardAvoidingView, Platform } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { Notice } from '../components/Notice';
import { useLocations } from '../location/LocationContext';
import { toSaveRequest } from '../utils/places';
import { errorMessage } from '../services/authApi';
import type { CountryCode, PlaceDetails, SavedLocation } from '../types/location';
import { DEFAULT_RADIUS, isCountryCode, savedRadius } from '../utils/geo';
import { RadiusPicker } from '../components/RadiusPicker';
import { PlaceSearch } from '../components/PlaceSearch';
import { ChipGroup, Field } from './sell/sellUi';

type Props = NativeStackScreenProps<RootStackParamList, 'AddressForm'>;

const LABELS = ['Home', 'Work', 'Other'];

/** PIN code (India), ZIP code (USA), Eircode (Ireland), postal code (Nigeria). */
export const postalCodeLabel = (country?: CountryCode | null) =>
  country === 'IN' ? 'PIN code' : country === 'US' ? 'ZIP code' : country === 'IE' ? 'Eircode' : 'Postal code';

/** A saved location as the chosen place of the form (older locations: only the place name and coordinates). */
const placeOf = (a: SavedLocation): PlaceDetails => ({
  placeId: a.placeId ?? null, formattedAddress: a.formattedAddress ?? '', lat: a.latitude, lon: a.longitude,
  label: a.placeName, area: a.area, city: a.city ?? a.placeName, state: a.state, countryCode: a.countryCode,
  postcode: a.postalCode, supported: true, source: 'local',
});

/** Add or edit a saved address. A new address is used straight away. */
export const AddressFormScreen: React.FC<Props> = ({ route, navigation }) => {
  const { addresses, saveAddress, country, location } = useLocations();
  const existing = addresses.find(a => a.id === route.params?.id);

  const [label, setLabel] = useState(existing?.label ?? 'Home');
  const [place, setPlace] = useState<PlaceDetails | null>(existing ? placeOf(existing) : null);
  const [addressLine, setAddressLine] = useState(existing?.addressLine ?? '');
  const [area, setArea] = useState(existing?.area ?? '');
  const [landmark, setLandmark] = useState(existing?.landmark ?? '');
  const [postalCode, setPostalCode] = useState(existing?.postalCode ?? '');
  // The radius for this address, in its country's unit (200 pre-selected for a new address).
  const [radius, setRadius] = useState<number | null>(existing ? savedRadius(existing).radiusValue : DEFAULT_RADIUS);
  const [busy, setBusy] = useState<'save' | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});

  // A searched place (or "Use my current location") fills the area, postcode and - for a street address - the
  // street; the user can still edit them and adds the house / flat number.
  const pickPlace = (p: PlaceDetails) => {
    setPlace(p);
    if (p.area) setArea(p.area);
    if (p.postcode) setPostalCode(p.postcode);
    if (p.street && !addressLine.trim()) setAddressLine(p.street);
    setErrors(e => ({ ...e, city: '', addressLine: p.street ? '' : e.addressLine }));
  };

  const save = async () => {
    const problems: Record<string, string> = {};
    if (!place) problems.city = 'Search for the place (or use your current location)';
    if (!addressLine.trim()) problems.addressLine = 'Enter the house / flat and street';
    if (radius == null) problems.radius = 'Choose a radius';
    setErrors(problems);
    if (Object.keys(problems).length > 0) return;

    setBusy('save');
    setNotice(null);
    try {
      await saveAddress(
        toSaveRequest(place!, {
          label,
          addressLine: addressLine.trim(),
          area: area.trim() || null,
          landmark: landmark.trim() || null,
          postalCode: postalCode.trim() || null,
          radiusValue: radius!,
          makePrimary: !existing,
        }),
        existing?.id,
      );
      // A new address is used at once (the list shows it as "In use").
      navigation.goBack();
    } catch (err) {
      setNotice(errorMessage(err, "Couldn't save the address. Please try again."));
    } finally {
      setBusy(null);
    }
  };

  const countryCode: CountryCode = isCountryCode(place?.countryCode) ? (place!.countryCode!.toUpperCase() as CountryCode) : country;

  return (
    <SafeAreaView style={styles.container}>
      <Header title={existing ? 'Edit address' : 'Add address'} onBack={() => navigation.goBack()} />
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
          {notice ? <Notice tone="info" message={notice} /> : null}

          <PlaceSearch
            label="Location"
            value={place}
            onPick={pickPlace}
            near={place ? { latitude: place.lat, longitude: place.lon } : location}
            error={errors.city || null}
          />

          <Field label="House / flat, building and street" value={addressLine} onChangeText={t => { setAddressLine(t); setErrors(e => ({ ...e, addressLine: '' })); }}
            placeholder="Flat 402, Sai Residency, Road No. 2" maxLength={200} />
          {errors.addressLine ? <Text style={styles.error}>{errors.addressLine}</Text> : null}
          <Field label="Area / locality" optional value={area} onChangeText={setArea} placeholder="Kukatpally" maxLength={100} />
          <Field label="Landmark" optional value={landmark} onChangeText={setLandmark} placeholder="Near JNTU metro station" maxLength={100} />
          <Field label={postalCodeLabel(countryCode)} optional value={postalCode} onChangeText={setPostalCode} maxLength={12}
            keyboardType={countryCode === 'IN' || countryCode === 'US' ? 'number-pad' : 'default'} autoCapitalize="characters" />

          <ChipGroup label="Save as" options={LABELS} value={LABELS.includes(label) ? label : 'Other'} onChange={setLabel} />
          {!LABELS.includes(label) || label === 'Other' ? (
            <Field label="Name" optional value={label === 'Other' ? '' : label} onChangeText={t => setLabel(t || 'Other')} placeholder="Workshop, Mum's place…" maxLength={40} />
          ) : null}

          {/* Units follow the address's country: miles in the USA, km elsewhere. */}
          <RadiusPicker value={radius} country={countryCode} onChange={setRadius} />
          {errors.radius && radius == null ? <Text style={styles.error}>{errors.radius}</Text> : null}

          <Text style={styles.hint}>
            Your full address is only shared with the seller when you choose delivery on an order you won.
          </Text>

          <TouchableOpacity style={styles.saveBtn} onPress={save} disabled={busy !== null}>
            {busy === 'save' ? <ActivityIndicator color={COLORS.text} /> : <Text style={styles.saveText}>{existing ? 'SAVE CHANGES' : 'SAVE AND USE THIS ADDRESS'}</Text>}
          </TouchableOpacity>
        </ScrollView>
      </KeyboardAvoidingView>

    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  flex: { flex: 1 },
  scroll: { padding: SPACING.md, paddingBottom: SPACING.xxl },
  error: { color: COLORS.danger, fontSize: 12, marginTop: -SPACING.xs, marginBottom: SPACING.sm },
  hint: { color: COLORS.textMuted, fontSize: 12, lineHeight: 17, marginTop: SPACING.sm },
  saveBtn: { backgroundColor: COLORS.primary, paddingVertical: 14, borderRadius: RADIUS.md, alignItems: 'center', marginTop: SPACING.lg },
  saveText: { color: COLORS.text, fontSize: 14, fontWeight: '900', letterSpacing: 0.5 },
});
