import React, { useState } from 'react';
import { View, Text, TextInput, TouchableOpacity, Modal, StyleSheet } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { PHONE_COUNTRIES, PhoneCountry, digitsOnly, maxLength } from '../utils/phone';

interface PhoneInputProps {
  country: PhoneCountry;
  onCountryChange: (country: PhoneCountry) => void;
  value: string;
  onChangeText: (digits: string) => void;
  error?: string | null;
}

/** Mobile number with a country code picker (India, USA, Nigeria). */
export const PhoneInput: React.FC<PhoneInputProps> = ({ country, onCountryChange, value, onChangeText, error }) => {
  const insets = useSafeAreaInsets();
  const [open, setOpen] = useState(false);

  // Nigerian numbers are often typed with a leading 0 (0803...), which isn't part of +234.
  const clean = (text: string, c: PhoneCountry) => {
    const digits = c.code === 'NG' ? digitsOnly(text).replace(/^0/, '') : digitsOnly(text);
    return digits.slice(0, maxLength(c));
  };

  return (
    <View>
      <Text style={styles.label}>Mobile Number</Text>
      <View style={[styles.inputWrap, !!error && styles.inputError]}>
        <TouchableOpacity style={styles.dial} onPress={() => setOpen(true)} activeOpacity={0.7}>
          <Text style={styles.flag}>{country.flag}</Text>
          <Text style={styles.dialText}>{country.dialCode}</Text>
          <Text style={styles.chevron}>▾</Text>
        </TouchableOpacity>
        <View style={styles.divider} />
        <TextInput
          style={styles.input}
          value={value}
          onChangeText={t => onChangeText(clean(t, country))}
          placeholder={country.example}
          placeholderTextColor={COLORS.textMuted}
          keyboardType="phone-pad"
          textContentType="telephoneNumber"
          autoComplete="tel"
          accessibilityLabel="Mobile number"
        />
      </View>
      {error ? <Text style={styles.error}>{error}</Text> : null}

      <Modal visible={open} transparent animationType="slide" onRequestClose={() => setOpen(false)}>
        <TouchableOpacity style={styles.backdrop} activeOpacity={1} onPress={() => setOpen(false)}>
          <View style={[styles.sheet, { paddingBottom: insets.bottom + SPACING.lg }]}>
            <Text style={styles.sheetTitle}>SELECT COUNTRY</Text>
            {PHONE_COUNTRIES.map(item => {
              const active = item.code === country.code;
              return (
                <TouchableOpacity
                  key={item.code}
                  style={[styles.row, active && styles.rowActive]}
                  onPress={() => {
                    onCountryChange(item);
                    onChangeText(clean(value, item));
                    setOpen(false);
                  }}
                >
                  <Text style={styles.rowFlag}>{item.flag}</Text>
                  <Text style={styles.rowName}>{item.name}</Text>
                  <Text style={[styles.rowDial, active && styles.rowDialActive]}>{item.dialCode}</Text>
                </TouchableOpacity>
              );
            })}
          </View>
        </TouchableOpacity>
      </Modal>
    </View>
  );
};

const styles = StyleSheet.create({
  label: {
    color: COLORS.textSecondary,
    fontSize: 14,
    fontWeight: '600',
    marginBottom: SPACING.xs,
    marginTop: SPACING.md,
  },
  inputWrap: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: COLORS.card,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  inputError: {
    borderColor: COLORS.danger,
  },
  dial: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    paddingHorizontal: SPACING.md,
    height: 48,
  },
  flag: {
    fontSize: 18,
  },
  dialText: {
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '700',
  },
  chevron: {
    color: COLORS.textMuted,
    fontSize: 12,
  },
  divider: {
    width: 1,
    alignSelf: 'stretch',
    marginVertical: 10,
    backgroundColor: COLORS.border,
  },
  input: {
    flex: 1,
    color: COLORS.text,
    fontSize: 16,
    height: 48,
    paddingHorizontal: SPACING.md,
  },
  error: {
    color: COLORS.danger,
    fontSize: 12,
    fontWeight: '600',
    marginTop: SPACING.xs,
  },
  backdrop: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.6)',
    justifyContent: 'flex-end',
  },
  sheet: {
    backgroundColor: COLORS.surface,
    borderTopLeftRadius: RADIUS.xl,
    borderTopRightRadius: RADIUS.xl,
    borderTopWidth: 1,
    borderColor: COLORS.border,
    paddingTop: SPACING.lg,
  },
  sheetTitle: {
    color: COLORS.textMuted,
    fontSize: 12,
    fontWeight: '800',
    letterSpacing: 1.5,
    paddingHorizontal: SPACING.lg,
    marginBottom: SPACING.sm,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: SPACING.md,
    paddingHorizontal: SPACING.lg,
    paddingVertical: 14,
  },
  rowActive: {
    backgroundColor: COLORS.badgeBg,
  },
  rowFlag: {
    fontSize: 22,
  },
  rowName: {
    flex: 1,
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '600',
  },
  rowDial: {
    color: COLORS.textSecondary,
    fontSize: 15,
    fontWeight: '700',
  },
  rowDialActive: {
    color: COLORS.primaryGlow,
  },
});
