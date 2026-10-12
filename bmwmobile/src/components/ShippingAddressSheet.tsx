import React, { useEffect, useState } from 'react';
import { View, Text, StyleSheet, Modal, ScrollView, TouchableOpacity, TextInput, ActivityIndicator, KeyboardAvoidingView, Platform } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { MarketApi } from '../services/marketApi';
import { ServerError } from '../services/api';
import { CheckboxRow } from './CheckboxRow';
import { countryName } from '../utils/geo';
import type { Address, FieldErrors, ShippingAddress } from '../types/listing';
import type { CountryCode } from '../types/location';

/** "Ravi Kumar · +91 98480 12345 · 12 MG Road, Kukatpally, Hyderabad 500072". */
export const addressText = (a: Address) =>
  [a.name, a.phone, [a.line1, a.line2, `${a.city} ${a.postcode}`.trim()].filter(Boolean).join(', ')].filter(Boolean).join(' · ');

const POSTCODE_LABEL: Record<string, string> = { IN: 'PIN code', US: 'ZIP code', IE: 'Eircode', NG: 'Postcode (optional)' };

const EMPTY = (countryCode: string): Address => ({ name: '', phone: '', line1: '', line2: '', city: '', postcode: '', countryCode });

/**
 * Where to ship: one of the buyer's saved addresses (the default first) or a new one, which is saved to the address
 * book (as the default if ticked). Shipping is within the seller's country, so the country is fixed.
 */
export const ShippingAddressSheet: React.FC<{
  visible: boolean;
  /** The seller's country (ships within it). */
  countryCode: string;
  onClose: () => void;
  /** Resolves with the server's answer; field errors are shown in the form. */
  onSubmit: (to: { addressId?: string; address?: Address }) => Promise<void>;
}> = ({ visible, countryCode, onClose, onSubmit }) => {
  const insets = useSafeAreaInsets();
  const [book, setBook] = useState<ShippingAddress[] | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState<Address>(EMPTY(countryCode));
  const [makeDefault, setMakeDefault] = useState(false);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!visible) return;
    setErrors({});
    setMessage(null);
    setForm(EMPTY(countryCode));
    MarketApi.shippingAddresses()
      .then(list => {
        // Only addresses in the seller's country can be used.
        const usable = list.filter(a => a.countryCode === countryCode);
        setBook(usable);
        setSelected((usable.find(a => a.isDefault) ?? usable[0])?.id ?? null);
        setAdding(usable.length === 0);
      })
      .catch(() => {
        setBook([]);
        setAdding(true);
      });
  }, [visible, countryCode]);

  const set = (patch: Partial<Address>) => setForm(f => ({ ...f, ...patch }));

  const submit = async () => {
    setBusy(true);
    setErrors({});
    setMessage(null);
    try {
      if (adding) {
        // Saved to the book first when it should become the default; otherwise the order saves it.
        if (makeDefault) {
          const saved = await MarketApi.addShippingAddress({ ...form, isDefault: true });
          await onSubmit({ addressId: saved.id });
        } else {
          await onSubmit({ address: form });
        }
      } else if (selected) {
        await onSubmit({ addressId: selected });
      }
    } catch (e) {
      if (e instanceof ServerError && Object.keys(e.fields).length > 0) setErrors(e.fields);
      else setMessage(e instanceof ServerError ? e.message : "Couldn't save the address. Please try again.");
    } finally {
      setBusy(false);
    }
  };

  const field = (key: keyof Address, label: string, props: Partial<React.ComponentProps<typeof TextInput>> = {}) => (
    <View style={styles.field}>
      <Text style={styles.label}>{label.toUpperCase()}</Text>
      <TextInput style={styles.input} placeholderTextColor={COLORS.textMuted} value={(form[key] as string) ?? ''}
        onChangeText={v => set({ [key]: v } as Partial<Address>)} {...props} />
      {errors[key]?.length ? <Text style={styles.error}>{errors[key].join(' ')}</Text> : null}
    </View>
  );

  const country = countryName(countryCode as CountryCode) ?? countryCode;

  return (
    <Modal visible={visible} animationType="slide" transparent onRequestClose={onClose}>
      <KeyboardAvoidingView style={styles.backdrop} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <View style={[styles.sheet, { paddingBottom: insets.bottom + SPACING.md }]}>
          <View style={styles.head}>
            <Text style={styles.title}>Ship to</Text>
            <TouchableOpacity onPress={onClose} hitSlop={10}>
              <Text style={styles.close}>✕</Text>
            </TouchableOpacity>
          </View>
          <Text style={styles.hint}>The seller ships within {country} only.</Text>

          {book === null ? (
            <ActivityIndicator color={COLORS.primaryGlow} style={styles.loader} />
          ) : (
            <ScrollView keyboardShouldPersistTaps="handled">
              {!adding ? (
                <>
                  {book.map(a => (
                    <TouchableOpacity key={a.id} style={[styles.option, selected === a.id && styles.optionActive]} onPress={() => setSelected(a.id)}
                      accessibilityState={{ selected: selected === a.id }}>
                      <Text style={styles.optionTitle}>{a.name}{a.isDefault ? '  · Default' : ''}</Text>
                      <Text style={styles.optionText}>{addressText(a)}</Text>
                    </TouchableOpacity>
                  ))}
                  <TouchableOpacity onPress={() => setAdding(true)}>
                    <Text style={styles.link}>+ Add a new address</Text>
                  </TouchableOpacity>
                </>
              ) : (
                <>
                  {field('name', 'Full name', { placeholder: 'Who receives it', autoCapitalize: 'words' })}
                  {field('phone', 'Phone', { placeholder: 'For the courier', keyboardType: 'phone-pad' })}
                  {field('line1', 'Address line 1', { placeholder: 'House / flat, building, street' })}
                  {field('line2', 'Address line 2 (optional)', { placeholder: 'Area, landmark' })}
                  {field('city', 'City', { autoCapitalize: 'words' })}
                  {field('postcode', POSTCODE_LABEL[countryCode] ?? 'Postcode', { autoCapitalize: 'characters' })}
                  <View style={styles.field}>
                    <Text style={styles.label}>COUNTRY</Text>
                    <Text style={styles.fixed}>{country}</Text>
                  </View>
                  <View style={styles.field}>
                    <CheckboxRow checked={makeDefault} onToggle={() => setMakeDefault(x => !x)} label="Use as my default address" />
                  </View>
                  {book.length > 0 ? (
                    <TouchableOpacity onPress={() => setAdding(false)}>
                      <Text style={styles.link}>← Choose a saved address</Text>
                    </TouchableOpacity>
                  ) : (
                    <Text style={styles.hint}>It's saved to your address book for next time.</Text>
                  )}
                </>
              )}
            </ScrollView>
          )}

          {message ? <Text style={styles.error}>{message}</Text> : null}
          <TouchableOpacity style={[styles.submit, (busy || (!adding && !selected)) && styles.disabled]} onPress={submit}
            disabled={busy || (!adding && !selected)}>
            <Text style={styles.submitText}>{busy ? 'Saving…' : 'Ship to this address'}</Text>
          </TouchableOpacity>
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
};

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.6)', justifyContent: 'flex-end' },
  sheet: { backgroundColor: COLORS.background, borderTopLeftRadius: RADIUS.lg, borderTopRightRadius: RADIUS.lg, padding: SPACING.md, maxHeight: '90%' },
  head: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  title: { color: COLORS.text, fontSize: 17, fontWeight: '800' },
  close: { color: COLORS.textMuted, fontSize: 20 },
  hint: { color: COLORS.textMuted, fontSize: 12, marginTop: 4, marginBottom: SPACING.sm },
  loader: { marginVertical: SPACING.lg },
  option: { borderWidth: 1, borderColor: COLORS.border, borderRadius: RADIUS.md, padding: SPACING.sm, gap: 2, marginBottom: SPACING.sm },
  optionActive: { borderColor: COLORS.primaryGlow, backgroundColor: COLORS.card },
  optionTitle: { color: COLORS.text, fontSize: 13, fontWeight: '800' },
  optionText: { color: COLORS.textSecondary, fontSize: 12 },
  link: { color: COLORS.primaryGlow, fontSize: 13, fontWeight: '800', paddingVertical: SPACING.sm },
  field: { marginTop: SPACING.sm },
  label: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1, marginBottom: 4 },
  input: {
    backgroundColor: COLORS.surface, borderRadius: RADIUS.md, color: COLORS.text, fontSize: 15, paddingHorizontal: SPACING.md,
    minHeight: 44, borderWidth: 1, borderColor: COLORS.border,
  },
  fixed: { color: COLORS.text, fontSize: 15, fontWeight: '700' },
  error: { color: COLORS.danger, fontSize: 12, fontWeight: '600', marginTop: 4 },
  submit: { marginTop: SPACING.md, backgroundColor: COLORS.primary, paddingVertical: 14, borderRadius: RADIUS.md, alignItems: 'center' },
  submitText: { color: COLORS.text, fontWeight: '800', fontSize: 14 },
  disabled: { opacity: 0.5 },
});
