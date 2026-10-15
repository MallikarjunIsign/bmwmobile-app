import React, { useState } from 'react';
import { Text, StyleSheet, ScrollView, TouchableOpacity, ActivityIndicator, KeyboardAvoidingView, Platform } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { Notice } from '../components/Notice';
import { useLocations } from '../location/LocationContext';
import { ApiService } from '../services/api';
import { LocationApi } from '../services/locationApi';
import { errorMessage } from '../services/authApi';
import type { City, CountryCode } from '../types/location';
import { countryName, DEFAULT_RADIUS, isCountryCode, savedRadius } from '../utils/geo';
import { RadiusPicker } from '../components/RadiusPicker';
import { getCurrentPosition, GpsError } from '../utils/gps';
import { ChipGroup, Field, PickerField, PickerModal } from './sell/sellUi';

type Props = NativeStackScreenProps<RootStackParamList, 'AddressForm'>;

const LABELS = ['Home', 'Work', 'Other'];

/** PIN code (India), ZIP code (USA), Eircode (Ireland), postal code (Nigeria). */
export const postalCodeLabel = (country?: CountryCode | null) =>
  country === 'IN' ? 'PIN code' : country === 'US' ? 'ZIP code' : country === 'IE' ? 'Eircode' : 'Postal code';

interface Place {
  city: string;
  countryCode: CountryCode | null;
  latitude: number;
  longitude: number;
}

/** Add or edit a saved address. A new address is used straight away. */
export const AddressFormScreen: React.FC<Props> = ({ route, navigation }) => {
  const { addresses, saveAddress, country } = useLocations();
  const existing = addresses.find(a => a.id === route.params?.id);

  const [label, setLabel] = useState(existing?.label ?? 'Home');
  const [place, setPlace] = useState<Place | null>(
    existing ? { city: existing.placeName, countryCode: existing.countryCode, latitude: existing.latitude, longitude: existing.longitude } : null,
  );
  const [addressLine, setAddressLine] = useState(existing?.addressLine ?? '');
  const [area, setArea] = useState(existing?.area ?? '');
  const [landmark, setLandmark] = useState(existing?.landmark ?? '');
  const [postalCode, setPostalCode] = useState(existing?.postalCode ?? '');
  // The radius for this address, in its country's unit (200 pre-selected for a new address).
  const [radius, setRadius] = useState<number | null>(existing ? savedRadius(existing).radiusValue : DEFAULT_RADIUS);
  const [pickCity, setPickCity] = useState(false);
  const [busy, setBusy] = useState<'gps' | 'save' | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});

  // Fill the city (and the exact point) from where the phone is now.
  const fillFromGps = async () => {
    setBusy('gps');
    setNotice(null);
    try {
      const pos = await getCurrentPosition({ prompt: true });
      const geo = await ApiService.reverseGeocode(pos.latitude, pos.longitude).catch(() => null);
      setPlace({
        city: geo?.city ?? place?.city ?? 'Current location',
        countryCode: isCountryCode(geo?.countryCode) ? (geo!.countryCode!.toUpperCase() as CountryCode) : place?.countryCode ?? country,
        latitude: pos.latitude,
        longitude: pos.longitude,
      });
      setErrors(e => ({ ...e, city: '' }));
    } catch (err) {
      setNotice(err instanceof GpsError ? err.message : "Couldn't read your position. Choose the city instead.");
    } finally {
      setBusy(null);
    }
  };

  const save = async () => {
    const problems: Record<string, string> = {};
    if (!place) problems.city = 'Choose the city (or use your current position)';
    if (!addressLine.trim()) problems.addressLine = 'Enter the house / flat and street';
    if (radius == null) problems.radius = 'Choose a radius';
    setErrors(problems);
    if (Object.keys(problems).length > 0) return;

    setBusy('save');
    setNotice(null);
    try {
      await saveAddress(
        {
          label,
          placeName: place!.city,
          latitude: place!.latitude,
          longitude: place!.longitude,
          countryCode: place!.countryCode,
          addressLine: addressLine.trim(),
          area: area.trim() || null,
          landmark: landmark.trim() || null,
          postalCode: postalCode.trim() || null,
          radiusValue: radius!,
          makePrimary: !existing,
        },
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

  const countryCode = place?.countryCode ?? country;

  return (
    <SafeAreaView style={styles.container}>
      <Header title={existing ? 'Edit address' : 'Add address'} onBack={() => navigation.goBack()} />
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
          <TouchableOpacity style={styles.gpsBtn} onPress={fillFromGps} disabled={busy !== null}>
            {busy === 'gps' ? <ActivityIndicator color={COLORS.primaryGlow} /> : <Text style={styles.gpsText}>🎯 Use my current position</Text>}
          </TouchableOpacity>
          {notice ? <Notice tone="info" message={notice} /> : null}

          <PickerField
            label="City"
            value={place ? [place.city, countryName(place.countryCode)].filter(Boolean).join(', ') : null}
            placeholder="Choose the city"
            onPress={() => setPickCity(true)}
          />
          {errors.city ? <Text style={styles.error}>{errors.city}</Text> : null}

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

      <PickerModal<City>
        visible={pickCity}
        title="Choose the city"
        placeholder="Search city"
        onClose={() => setPickCity(false)}
        load={text => LocationApi.searchCities(text, countryCode)}
        render={c => ({ key: `${c.countryCode}-${c.name}`, title: c.name, subtitle: countryName(c.countryCode) ?? undefined })}
        onPick={c => {
          setPlace({ city: c.name, countryCode: c.countryCode, latitude: c.latitude, longitude: c.longitude });
          setErrors(e => ({ ...e, city: '' }));
          setPickCity(false);
        }}
      />
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  flex: { flex: 1 },
  scroll: { padding: SPACING.md, paddingBottom: SPACING.xxl },
  gpsBtn: { borderWidth: 1, borderColor: COLORS.primaryGlow, borderRadius: RADIUS.md, paddingVertical: 12, alignItems: 'center', marginBottom: SPACING.md },
  gpsText: { color: COLORS.primaryGlow, fontWeight: '800', fontSize: 14 },
  error: { color: COLORS.danger, fontSize: 12, marginTop: -SPACING.xs, marginBottom: SPACING.sm },
  hint: { color: COLORS.textMuted, fontSize: 12, lineHeight: 17, marginTop: SPACING.sm },
  saveBtn: { backgroundColor: COLORS.primary, paddingVertical: 14, borderRadius: RADIUS.md, alignItems: 'center', marginTop: SPACING.lg },
  saveText: { color: COLORS.text, fontSize: 14, fontWeight: '900', letterSpacing: 0.5 },
});
