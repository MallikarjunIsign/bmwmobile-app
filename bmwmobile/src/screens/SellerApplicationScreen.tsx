import React, { useMemo, useState } from 'react';
import { View, Text, StyleSheet, KeyboardAvoidingView, Platform, ScrollView, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { FormInput } from '../components/FormInput';
import { PhoneInput } from '../components/PhoneInput';
import { Notice } from '../components/Notice';
import { PrimaryButton } from '../components/PrimaryButton';
import { useAuth } from '../auth/AuthContext';
import { useLocations } from '../location/LocationContext';
import { config } from '../config';
import { ApiError, errorMessage } from '../services/authApi';
import { SellerApplicationApi, type BusinessType, type Sells } from '../services/sellerApplicationApi';
import { DEFAULT_COUNTRY, PHONE_COUNTRIES, PhoneCountry, toE164, validatePhone } from '../utils/phone';
import { validateEmail, validateFullName } from '../utils/validation';

type Props = NativeStackScreenProps<RootStackParamList, 'SellerApplication'>;

const BUSINESS_TYPES: { value: BusinessType; label: string }[] = [
  { value: 'Individual', label: 'Individual' },
  { value: 'Shop', label: 'Parts shop' },
  { value: 'Dealer', label: 'Car dealer' },
  { value: 'Garage', label: 'Garage / workshop' },
  { value: 'Scrapyard', label: 'Scrapyard / breaker' },
  { value: 'Other', label: 'Other' },
];
const SELLS: { value: Sells; label: string }[] = [
  { value: 'Parts', label: 'Parts' },
  { value: 'Vehicles', label: 'Vehicles' },
  { value: 'Both', label: 'Both' },
];
/** The business tax number's local name. */
const TAX_LABEL: Record<string, string> = { IN: 'GSTIN', US: 'EIN', NG: 'TIN', IE: 'VAT number' };

type Field = 'fullName' | 'email' | 'phone' | 'city' | 'businessType' | 'sells' | 'businessName';
type Errors = Partial<Record<Field, string | null>>;

/** +919876543210 → India + "9876543210" (for a signed-in user's saved number). */
function splitPhone(e164?: string | null): { country: PhoneCountry; national: string } {
  const match = e164 ? PHONE_COUNTRIES.find(c => e164.startsWith(c.dialCode)) : undefined;
  return match ? { country: match, national: e164!.slice(match.dialCode.length) } : { country: DEFAULT_COUNTRY, national: '' };
}

const Choice = ({ label, active, onPress }: { label: string; active: boolean; onPress: () => void }) => (
  <TouchableOpacity
    style={[styles.choice, active && styles.choiceActive]}
    onPress={onPress}
    activeOpacity={0.8}
    accessibilityRole="radio"
    accessibilityState={{ selected: active }}
  >
    <Text style={[styles.choiceText, active && styles.choiceTextActive]}>{label}</Text>
  </TouchableOpacity>
);

/**
 * "Want to sell?": a form with contact and business details instead of an email. It is stored on the server and the
 * admins are emailed; an admin creates the seller account (we email the seller a code to set their password).
 * Signed in (Profile) the details are pre-filled and the request is linked to the account.
 */
export const SellerApplicationScreen: React.FC<Props> = ({ navigation, route }) => {
  const { user } = useAuth();
  const { location } = useLocations();
  const phone0 = useMemo(() => splitPhone(user?.phoneNumber), [user?.phoneNumber]);

  const [fullName, setFullName] = useState(user?.fullName ?? '');
  const [email, setEmail] = useState(user?.email ?? route.params?.email ?? '');
  const [country, setCountry] = useState<PhoneCountry>(
    PHONE_COUNTRIES.find(c => c.code === location?.countryCode) ?? phone0.country,
  );
  const [phone, setPhone] = useState(phone0.national);
  const [city, setCity] = useState(location?.city ?? '');
  const [businessType, setBusinessType] = useState<BusinessType | null>(null);
  const [sells, setSells] = useState<Sells | null>(null);
  const [businessName, setBusinessName] = useState('');
  const [businessAddress, setBusinessAddress] = useState('');
  const [taxId, setTaxId] = useState('');
  const [message, setMessage] = useState('');
  const [errors, setErrors] = useState<Errors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [sent, setSent] = useState(false);

  const individual = businessType === 'Individual';
  const clear = (field: Field) => setErrors(e => (e[field] ? { ...e, [field]: null } : e));

  const submit = async () => {
    setFormError(null);
    const next: Errors = {
      fullName: validateFullName(fullName),
      email: validateEmail(email),
      phone: validatePhone(country, phone),
      city: city.trim().length >= 2 ? null : 'Enter your city',
      businessType: businessType ? null : 'Choose what kind of seller you are',
      sells: sells ? null : 'Choose what you want to sell',
      businessName: individual || businessName.trim().length >= 2 ? null : 'Enter your business name',
    };
    setErrors(next);
    if (Object.values(next).some(Boolean)) {
      setFormError('Please fix the fields marked in red.');
      return;
    }

    setSubmitting(true);
    try {
      await SellerApplicationApi.submit({
        fullName: fullName.trim(),
        email: email.trim(),
        phoneNumber: toE164(country, phone),
        countryCode: country.code,
        city: city.trim(),
        businessType: businessType!,
        sells: sells!,
        businessName: businessName.trim() || undefined,
        businessAddress: businessAddress.trim() || undefined,
        taxId: taxId.trim() || undefined,
        message: message.trim() || undefined,
      });
      setSent(true);
    } catch (err) {
      setFormError(
        err instanceof ApiError && (err.code === 'APPLICATION_PENDING' || err.code === 'ALREADY_SELLER')
          ? err.message
          : errorMessage(err, "Couldn't send your request. Please try again."),
      );
    } finally {
      setSubmitting(false);
    }
  };

  if (sent) {
    return (
      <SafeAreaView style={styles.container}>
        <Header title="Request sent" onBack={() => navigation.goBack()} />
        <View style={styles.done}>
          <Text style={styles.doneIcon}>✅</Text>
          <Text style={styles.doneTitle}>Thanks, {fullName.trim().split(' ')[0]}!</Text>
          <Text style={styles.doneText}>
            Your request to sell on {config.APP_NAME} has been sent to our team. We'll review it and contact you at {email.trim()} or{' '}
            {toE164(country, phone)}.
          </Text>
          <Text style={styles.doneText}>
            When your seller account is ready{user ? ', the Sell tab appears in the app' : ", we'll email you a code to set your password"}.
          </Text>
          <PrimaryButton label="DONE" onPress={() => navigation.goBack()} style={styles.doneBtn} />
        </View>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container}>
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <Header title="Become a seller" subtitle={`Tell us about you and your business – our team sets up ${config.APP_NAME} seller accounts`} onBack={() => navigation.goBack()} />
        <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
          <Text style={styles.section}>CONTACT</Text>
          <FormInput label="Full name" value={fullName} onChangeText={t => { setFullName(t); clear('fullName'); }}
            error={errors.fullName} autoCapitalize="words" placeholder="Your name" />
          <FormInput label="Email" value={email} onChangeText={t => { setEmail(t); clear('email'); }} error={errors.email}
            keyboardType="email-address" autoCapitalize="none" autoCorrect={false} placeholder="you@example.com" />
          <Text style={styles.label}>Mobile number</Text>
          <PhoneInput country={country} onCountryChange={setCountry} value={phone} onChangeText={t => { setPhone(t); clear('phone'); }} error={errors.phone} />
          <FormInput label={`City (${country.name})`} value={city} onChangeText={t => { setCity(t); clear('city'); }} error={errors.city}
            autoCapitalize="words" placeholder="e.g. Hyderabad" />

          <Text style={styles.section}>BUSINESS</Text>
          <Text style={styles.label}>You are</Text>
          <View style={styles.choices}>
            {BUSINESS_TYPES.map(t => (
              <Choice key={t.value} label={t.label} active={businessType === t.value} onPress={() => { setBusinessType(t.value); clear('businessType'); clear('businessName'); }} />
            ))}
          </View>
          {errors.businessType ? <Text style={styles.error}>{errors.businessType}</Text> : null}

          <Text style={styles.label}>You want to sell</Text>
          <View style={styles.choices}>
            {SELLS.map(s => <Choice key={s.value} label={s.label} active={sells === s.value} onPress={() => { setSells(s.value); clear('sells'); }} />)}
          </View>
          {errors.sells ? <Text style={styles.error}>{errors.sells}</Text> : null}

          <FormInput label={individual ? 'Business name (optional)' : 'Business name'} value={businessName}
            onChangeText={t => { setBusinessName(t); clear('businessName'); }} error={errors.businessName} autoCapitalize="words"
            placeholder="e.g. Ravi Auto Parts" />
          <FormInput label="Business address (optional)" value={businessAddress} onChangeText={setBusinessAddress}
            placeholder="Shop number, street, area" />
          <FormInput label={`${TAX_LABEL[country.code] ?? 'Tax number'} (optional)`} value={taxId} onChangeText={setTaxId}
            autoCapitalize="characters" autoCorrect={false} maxLength={30} />
          <FormInput label="Anything else? (optional)" value={message} onChangeText={setMessage} multiline maxLength={1000}
            placeholder="What you sell, how many items, makes you specialise in…" style={styles.message} />

          {formError ? <Notice tone="error" message={formError} /> : null}
          <PrimaryButton label="SEND TO OUR TEAM" onPress={submit} loading={submitting} style={styles.submit} />
          <Text style={styles.small}>We only use these details to set up your seller account and contact you about it.</Text>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  flex: { flex: 1 },
  scroll: { padding: SPACING.md, paddingBottom: SPACING.xxl },
  section: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1, marginTop: SPACING.md, marginBottom: SPACING.sm },
  label: { color: COLORS.textSecondary, fontSize: 13, fontWeight: '600', marginBottom: SPACING.xs, marginTop: SPACING.sm },
  choices: { flexDirection: 'row', flexWrap: 'wrap', gap: SPACING.xs, marginBottom: SPACING.sm },
  choice: {
    backgroundColor: COLORS.card, borderRadius: RADIUS.full, borderWidth: 1, borderColor: COLORS.border,
    paddingHorizontal: SPACING.md, paddingVertical: SPACING.xs + 2,
  },
  choiceActive: { backgroundColor: COLORS.primary, borderColor: COLORS.primaryGlow },
  choiceText: { color: COLORS.textSecondary, fontSize: 13, fontWeight: '600' },
  choiceTextActive: { color: COLORS.text, fontWeight: '800' },
  error: { color: COLORS.danger, fontSize: 12, marginBottom: SPACING.sm },
  message: { minHeight: 80, textAlignVertical: 'top' },
  submit: { marginTop: SPACING.md },
  small: { color: COLORS.textMuted, fontSize: 11, textAlign: 'center', marginTop: SPACING.sm },
  done: { flex: 1, padding: SPACING.lg, alignItems: 'center', justifyContent: 'center', gap: SPACING.md },
  doneIcon: { fontSize: 48 },
  doneTitle: { color: COLORS.text, fontSize: 20, fontWeight: '900' },
  doneText: { color: COLORS.textSecondary, fontSize: 14, textAlign: 'center', lineHeight: 20 },
  doneBtn: { alignSelf: 'stretch', marginTop: SPACING.md },
});
