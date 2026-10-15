import React, { useEffect, useState } from 'react';
import { Modal, View, Text, StyleSheet, TouchableOpacity, ScrollView, ActivityIndicator, KeyboardAvoidingView, Platform } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { RadiusPicker } from './RadiusPicker';
import { Notice } from './Notice';
import { errorMessage } from '../services/authApi';
import type { CountryCode } from '../types/location';

interface Props {
  visible: boolean;
  title: string;
  /** "Houston, USA" – where the radius is measured from. */
  subtitle?: string | null;
  /** The radius to start with, in the location's unit. */
  initial: number;
  country?: CountryCode | null;
  confirmLabel: string;
  /** Save / use it. Throws to show the error in the sheet. */
  onConfirm: (radiusValue: number) => Promise<void>;
  onClose: () => void;
  /** Extra rows under the button (Home: the saved locations, "Use my current location"). */
  children?: React.ReactNode;
}

/** A bottom sheet with the radius picker: "Use my current location" and Home's location line. */
export const RadiusSheet: React.FC<Props> = ({ visible, title, subtitle, initial, country, confirmLabel, onConfirm, onClose, children }) => {
  const insets = useSafeAreaInsets();
  const [value, setValue] = useState<number | null>(initial);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (visible) {
      setValue(initial);
      setError(null);
    }
  }, [visible, initial]);

  const confirm = async () => {
    if (value == null) return;
    setBusy(true);
    setError(null);
    try {
      await onConfirm(value);
    } catch (e) {
      setError(errorMessage(e, "Couldn't save the radius. Please try again."));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal visible={visible} animationType="slide" transparent onRequestClose={onClose}>
      <KeyboardAvoidingView style={styles.backdrop} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <View style={[styles.sheet, { paddingBottom: insets.bottom + SPACING.md }]}>
          <View style={styles.head}>
            <Text style={styles.title}>{title}</Text>
            <TouchableOpacity onPress={onClose} hitSlop={10} accessibilityLabel="Close">
              <Text style={styles.close}>✕</Text>
            </TouchableOpacity>
          </View>
          {subtitle ? <Text style={styles.subtitle}>📍 {subtitle}</Text> : null}
          <ScrollView keyboardShouldPersistTaps="handled">
            <RadiusPicker value={value} country={country} onChange={setValue} />
            {error ? <Notice tone="error" message={error} /> : null}
            <TouchableOpacity style={[styles.button, (value == null || busy) && styles.disabled]} onPress={confirm}
              disabled={value == null || busy} accessibilityRole="button">
              {busy ? <ActivityIndicator color={COLORS.text} /> : <Text style={styles.buttonText}>{confirmLabel}</Text>}
            </TouchableOpacity>
            {children}
          </ScrollView>
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
  subtitle: { color: COLORS.textSecondary, fontSize: 13, marginTop: 4 },
  button: { backgroundColor: COLORS.primary, paddingVertical: 14, borderRadius: RADIUS.md, alignItems: 'center', marginTop: SPACING.md },
  buttonText: { color: COLORS.text, fontSize: 14, fontWeight: '900', letterSpacing: 0.5 },
  disabled: { opacity: 0.5 },
});
