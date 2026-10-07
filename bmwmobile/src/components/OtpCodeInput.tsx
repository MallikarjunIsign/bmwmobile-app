import React, { useRef } from 'react';
import { View, Text, TextInput, TouchableOpacity, StyleSheet, type TextInputInstance } from 'react-native';
import { COLORS, RADIUS } from '../theme/colors';

interface OtpCodeInputProps {
  value: string;
  onChange: (value: string) => void;
  onComplete?: (value: string) => void;
  error?: boolean;
  autoFocus?: boolean;
  length?: number;
}

/** Six code boxes backed by one hidden input, so paste and SMS / email autofill work. */
export const OtpCodeInput: React.FC<OtpCodeInputProps> = ({ value, onChange, onComplete, error, autoFocus = true, length = 6 }) => {
  const ref = useRef<TextInputInstance>(null);
  const digits = value.split('');

  return (
    <TouchableOpacity style={styles.row} activeOpacity={1} onPress={() => ref.current?.focus()} accessibilityLabel="Verification code">
      {Array.from({ length }).map((_, i) => {
        const active = i === Math.min(value.length, length - 1);
        return (
          <View key={i} style={[styles.box, active && styles.boxActive, !!digits[i] && styles.boxFilled, error && styles.boxError]}>
            <Text style={styles.digit}>{digits[i] ?? ''}</Text>
          </View>
        );
      })}
      <TextInput
        ref={ref}
        value={value}
        onChangeText={t => {
          const v = t.replace(/\D/g, '').slice(0, length);
          onChange(v);
          if (v.length === length) onComplete?.(v);
        }}
        keyboardType="number-pad"
        textContentType="oneTimeCode"
        autoComplete="sms-otp"
        maxLength={length}
        autoFocus={autoFocus}
        caretHidden
        style={styles.hidden}
        testID="otp-input"
      />
    </TouchableOpacity>
  );
};

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    justifyContent: 'center',
    gap: 8,
    alignSelf: 'stretch',
  },
  box: {
    width: 48,
    height: 58,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.border,
    backgroundColor: COLORS.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  boxActive: {
    borderColor: COLORS.primaryGlow,
    borderWidth: 1.5,
  },
  boxFilled: {
    backgroundColor: COLORS.card,
  },
  boxError: {
    borderColor: COLORS.danger,
  },
  digit: {
    color: COLORS.primaryGlow,
    fontSize: 26,
    fontWeight: '900',
  },
  hidden: {
    position: 'absolute',
    opacity: 0,
    width: 1,
    height: 1,
  },
});
