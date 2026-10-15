import React, { useState } from 'react';
import { View, Text, StyleSheet, TextInput, ScrollView, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { Notice } from '../components/Notice';
import { ApiService, ServerError } from '../services/api';
import { useCurrencyLabel } from '../location/useMoney';
import { useLocations } from '../location/LocationContext';
import { FEATURES } from '../config';
import { formatRadius, REGIONS } from '../utils/geo';

type Props = NativeStackScreenProps<RootStackParamList, 'OrderPartRequest'>;

/**
 * Custom order: "There are no parts available for this search – create a custom order and get notified when the
 * part is available". Pre-filled from the search; saved with the buyer's automatic location and radius so only
 * listings nearby trigger the notification.
 */
export const OrderPartRequestScreen: React.FC<Props> = ({ route, navigation }) => {
  const currencyLabel = useCurrencyLabel();
  const { location, country } = useLocations();
  const { prefillMake, prefillModel, prefillPart, prefillYear, prefillPartNumber, searchText } = route.params || {};

  const [partName, setPartName] = useState(prefillPart || searchText || '');
  const [partNumber, setPartNumber] = useState(prefillPartNumber || '');
  const [make, setMake] = useState(prefillMake || '');
  const [model, setModel] = useState(prefillModel || '');
  const [year, setYear] = useState(prefillYear ? String(prefillYear) : '');
  const [specs, setSpecs] = useState('');
  const [maxBudget, setMaxBudget] = useState('');
  const [preferredPort, setPreferredPort] = useState('Lagos Apapa Port');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async () => {
    if (!partName.trim() && !partNumber.trim()) {
      setError('Tell us which part you need (a part name or a part number).');
      return;
    }
    const yearValue = year.trim() ? parseInt(year, 10) : 0;
    if (year.trim() && (Number.isNaN(yearValue) || yearValue < 1900 || yearValue > new Date().getFullYear() + 1)) {
      setError('Enter a valid year, or leave it empty for any year.');
      return;
    }

    setSubmitting(true);
    setError(null);
    try {
      const request = await ApiService.createPartRequest({
        searchText: searchText || undefined,
        partName: partName.trim() || undefined,
        partNumber: partNumber.trim() || undefined,
        make: make.trim() || undefined,
        model: model.trim() || undefined,
        year: yearValue || undefined,
        specs: specs.trim() || undefined,
        maxBudget: parseFloat(maxBudget) || undefined,
        // Only listings within the buyer's radius will match (country-wide when the location is unknown).
        locationName: location?.city ?? undefined,
        countryCode: location?.countryCode ?? country,
        latitude: location?.latitude,
        longitude: location?.longitude,
        radiusKm: location?.radiusKm,
      });
      navigation.replace('RequestSubmitted', { request });
    } catch (err) {
      setError(err instanceof ServerError ? err.message : "Your custom order couldn't be saved. Please try again.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <SafeAreaView style={styles.container}>
      <Header
        title="Create a Custom Order"
        subtitle="We'll notify you when a matching part is listed"
        onBack={() => navigation.goBack()}
      />

      <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
        {searchText ? <Text style={styles.searchedFor}>You searched for “{searchText}”</Text> : null}

        <Text style={styles.label}>PART YOU NEED</Text>
        <TextInput style={styles.input} value={partName} onChangeText={setPartName} placeholder="e.g. Front bumper" placeholderTextColor={COLORS.textMuted} />

        <Text style={styles.label}>PART NUMBER (OPTIONAL)</Text>
        <TextInput style={styles.input} value={partNumber} onChangeText={setPartNumber} autoCapitalize="characters" placeholder="e.g. 52119-0K935" placeholderTextColor={COLORS.textMuted} />

        <Text style={styles.label}>VEHICLE MAKE (OPTIONAL)</Text>
        <TextInput style={styles.input} value={make} onChangeText={setMake} placeholder="Any make" placeholderTextColor={COLORS.textMuted} />

        <Text style={styles.label}>MODEL (OPTIONAL)</Text>
        <TextInput style={styles.input} value={model} onChangeText={setModel} placeholder="Any model" placeholderTextColor={COLORS.textMuted} />

        <Text style={styles.label}>YEAR (OPTIONAL)</Text>
        <TextInput style={styles.input} value={year} onChangeText={setYear} keyboardType="number-pad" placeholder="Any year" placeholderTextColor={COLORS.textMuted} />

        <Text style={styles.label}>NOTES (OPTIONAL)</Text>
        <TextInput style={[styles.input, styles.multiline]} value={specs} onChangeText={setSpecs} multiline placeholder="Condition, colour, anything else" placeholderTextColor={COLORS.textMuted} />

        <Text style={styles.label}>MAXIMUM BUDGET ({currencyLabel}, OPTIONAL)</Text>
        <TextInput style={styles.input} value={maxBudget} onChangeText={setMaxBudget} keyboardType="numeric" />

        {/* Container ports (hidden while FEATURES.containers is off) */}
        {FEATURES.containers ? (
          <>
            <Text style={styles.label}>TARGET NIGERIAN CLEARING PORT</Text>
            <View style={styles.portRow}>
              {['Lagos Apapa Port', 'Lagos Tin Can', 'Onne Port'].map(p => (
                <TouchableOpacity 
                  key={p} 
                  style={[styles.portChip, preferredPort.includes(p) && styles.portChipActive]}
                  onPress={() => setPreferredPort(p)}
                >
                  <Text style={[styles.portText, preferredPort.includes(p) && styles.portTextActive]}>{p}</Text>
                </TouchableOpacity>
              ))}
            </View>
          </>
        ) : null}

        <View style={styles.feasibilityBox}>
          <Text style={styles.feaTitle}>🔔 GET NOTIFIED</Text>
          <Text style={styles.feaText}>
            {location
              ? `When a matching part is listed for pickup within ${formatRadius(location.radiusKm, country)}${location.city ? ` of ${location.city}` : ''}, or with shipping anywhere in ${REGIONS[country].name}, we'll let you know.`
              : "When a matching part is listed, we'll let you know."}
          </Text>
        </View>

        {error ? <Notice tone="error" message={error} /> : null}

        <TouchableOpacity 
          style={styles.submitBtn} 
          onPress={handleSubmit} 
          disabled={submitting}
          activeOpacity={0.8}
        >
          <Text style={styles.submitText}>{submitting ? 'CREATING ORDER...' : 'CREATE CUSTOM ORDER'}</Text>
        </TouchableOpacity>
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
  label: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1,
    marginTop: SPACING.md,
    marginBottom: SPACING.xs,
  },
  searchedFor: {
    color: COLORS.textSecondary,
    fontSize: 13,
    fontStyle: 'italic',
  },
  multiline: {
    height: 70,
    textAlignVertical: 'top',
    paddingTop: SPACING.sm,
  },
  input: {
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.md,
    color: COLORS.text,
    fontSize: 15,
    paddingHorizontal: SPACING.md,
    height: 48,
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  portRow: {
    flexDirection: 'row',
    gap: SPACING.xs,
    marginVertical: SPACING.xs,
  },
  portChip: {
    backgroundColor: COLORS.card,
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.xs + 2,
    borderRadius: RADIUS.full,
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  portChipActive: {
    backgroundColor: COLORS.primary,
    borderColor: COLORS.primaryGlow,
  },
  portText: {
    color: COLORS.textMuted,
    fontSize: 12,
    fontWeight: '600',
  },
  portTextActive: {
    color: COLORS.text,
    fontWeight: '700',
  },
  feasibilityBox: {
    backgroundColor: COLORS.card,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
    marginVertical: SPACING.lg,
  },
  feaTitle: {
    color: COLORS.primaryGlow,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: 4,
  },
  feaText: {
    color: COLORS.textSecondary,
    fontSize: 12,
    lineHeight: 16,
  },
  bold: {
    color: COLORS.text,
    fontWeight: '700',
  },
  submitBtn: {
    backgroundColor: COLORS.primary,
    marginTop: SPACING.lg,
    paddingVertical: 16,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  submitText: {
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '900',
    letterSpacing: 1,
  },
});
