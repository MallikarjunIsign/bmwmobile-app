import React, { useEffect, useState } from 'react';
import { Text, StyleSheet, View } from 'react-native';
import { COLORS, SPACING } from '../theme/colors';
import { ChipGroup, Field } from '../screens/sell/sellUi';
import type { CountryCode } from '../types/location';
import { distanceUnit, isValidRadius, MAX_RADIUS, MIN_RADIUS, RADIUS_CHOICES } from '../utils/geo';

interface Props {
  /** The chosen radius in the location's unit; null while the typed value isn't valid. */
  value: number | null;
  /** The location's country: miles in the USA, km elsewhere. */
  country?: CountryCode | null;
  onChange: (value: number | null) => void;
  label?: string;
}

/**
 * "Show pickup-only parts within …": quick choices 25 / 50 / 100 / 200 / 500 (mi in the USA, km elsewhere) or any
 * number from 5 to 1,000. Listings that ship are shown from the whole country whatever the radius.
 */
export const RadiusPicker: React.FC<Props> = ({ value, country, onChange, label = 'Search radius' }) => {
  const unit = distanceUnit(country);
  const [text, setText] = useState(value != null ? String(value) : '');
  // A quick choice (or a new value from outside) shows in the field too.
  useEffect(() => {
    if (value != null && String(value) !== text) setText(String(value));
  }, [value]); // eslint-disable-line react-hooks/exhaustive-deps

  const typed = parseInt(text, 10);
  const invalid = text !== '' && !isValidRadius(typed);
  const custom = value != null && !(RADIUS_CHOICES as readonly number[]).includes(value);

  return (
    <View testID="radius-picker">
      <ChipGroup
        label={label}
        options={RADIUS_CHOICES.map(n => ({ value: String(n), label: `${n} ${unit}` }))}
        value={value != null && !custom ? String(value) : null}
        onChange={(v: string) => {
          setText(v);
          onChange(parseInt(v, 10));
        }}
      />
      <Field
        label="Or type a radius"
        value={text}
        suffix={unit}
        keyboardType="number-pad"
        maxLength={4}
        placeholder={`${MIN_RADIUS}–${MAX_RADIUS.toLocaleString()}`}
        accessibilityLabel={`Radius in ${unit === 'mi' ? 'miles' : 'km'}`}
        onChangeText={t => {
          const digits = t.replace(/[^0-9]/g, '');
          setText(digits);
          const n = parseInt(digits, 10);
          onChange(isValidRadius(n) ? n : null);
        }}
      />
      {invalid || text === '' ? (
        <Text style={styles.error}>Enter a radius from {MIN_RADIUS} to {MAX_RADIUS.toLocaleString()} {unit}.</Text>
      ) : (
        <Text style={styles.hint}>
          Pickup-only parts within {value} {unit}. Parts that ship are shown from anywhere in your country.
        </Text>
      )}
    </View>
  );
};

const styles = StyleSheet.create({
  hint: { color: COLORS.textMuted, fontSize: 12, lineHeight: 17, marginTop: SPACING.xs },
  error: { color: COLORS.danger, fontSize: 12, marginTop: SPACING.xs },
});
