import React, { useEffect, useState } from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { Field, styles as ui } from './sellUi';
import { SPACING } from '../../theme/colors';

export interface PackageValue {
  weightLb?: number | null;
  lengthIn?: number | null;
  widthIn?: number | null;
  heightIn?: number | null;
}

const KEYS = ['weightLb', 'lengthIn', 'widthIn', 'heightIn'] as const;
type Key = (typeof KEYS)[number];

const text = (n?: number | null) => (n == null ? '' : String(n));

/**
 * US shipping: the package weight (lb) and box size (inches) USPS prices use. Pre-filled from the part type
 * (defaults); the seller can change them. Only whole and decimal numbers are kept.
 */
export const PackageFields: React.FC<{
  value: PackageValue;
  defaults: PackageValue;
  onChange: (patch: PackageValue) => void;
}> = ({ value, defaults, onChange }) => {
  const shown = (k: Key) => value[k] ?? defaults[k];
  // The text being typed ("0." is fine while typing); synced from the value when it changes elsewhere.
  const [draft, setDraft] = useState<Record<Key, string>>(() =>
    Object.fromEntries(KEYS.map(k => [k, text(shown(k))])) as Record<Key, string>);
  useEffect(() => {
    setDraft(d => Object.fromEntries(KEYS.map(k => [k, Number(d[k]) === shown(k) ? d[k] : text(shown(k))])) as Record<Key, string>);
  }, [value.weightLb, value.lengthIn, value.widthIn, value.heightIn, defaults.weightLb, defaults.lengthIn, defaults.widthIn, defaults.heightIn]); // eslint-disable-line react-hooks/exhaustive-deps

  const set = (k: Key) => (raw: string) => {
    const clean = raw.replace(',', '.').replace(/[^0-9.]/g, '');
    setDraft(d => ({ ...d, [k]: clean }));
    const n = parseFloat(clean);
    if (!Number.isNaN(n) && n > 0) onChange({ [k]: n });
  };

  return (
    <View style={ui.field}>
      <Text style={ui.label}>PACKAGE (FOR THE USPS PRICE)</Text>
      <Field label="Weight" suffix="lb" keyboardType="decimal-pad" value={draft.weightLb} onChangeText={set('weightLb')}
        accessibilityLabel="Package weight in pounds" />
      <View style={styles.row}>
        <View style={styles.cell}>
          <Field label="Length" suffix="in" keyboardType="decimal-pad" value={draft.lengthIn} onChangeText={set('lengthIn')}
            accessibilityLabel="Box length in inches" />
        </View>
        <View style={styles.cell}>
          <Field label="Width" suffix="in" keyboardType="decimal-pad" value={draft.widthIn} onChangeText={set('widthIn')}
            accessibilityLabel="Box width in inches" />
        </View>
        <View style={styles.cell}>
          <Field label="Height" suffix="in" keyboardType="decimal-pad" value={draft.heightIn} onChangeText={set('heightIn')}
            accessibilityLabel="Box height in inches" />
        </View>
      </View>
      <Text style={ui.hint}>
        Pre-filled for this part – change it to your packed box. Buyers pay USPS Ground Advantage retail (official USPS
        prices by weight and distance); over 70 lb or 130 in length + girth goes by freight.
      </Text>
    </View>
  );
};

const styles = StyleSheet.create({
  row: { flexDirection: 'row', gap: SPACING.sm },
  cell: { flex: 1 },
});
