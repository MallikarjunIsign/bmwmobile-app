import React, { forwardRef, useState } from 'react';
import { View, Text, TextInput, TouchableOpacity, StyleSheet, TextInputProps, type TextInputInstance } from 'react-native';
import { COLORS, SPACING, RADIUS } from '../theme/colors';

interface FormInputProps extends TextInputProps {
  label: string;
  error?: string | null;
  /** Shows SHOW / HIDE for password fields. */
  secureToggle?: boolean;
}

/** Label + input in the app's dark card style (same look as the original Login screen). */
export const FormInput = forwardRef<TextInputInstance, FormInputProps>(({ label, error, secureToggle, style, onFocus, onBlur, ...rest }, ref) => {
  const [focused, setFocused] = useState(false);
  const [hidden, setHidden] = useState(true);

  return (
    <View>
      <Text style={styles.label}>{label}</Text>
      <View style={[styles.inputWrap, focused && styles.inputFocused, !!error && styles.inputError]}>
        <TextInput
          {...rest}
          ref={ref}
          style={[styles.input, style]}
          placeholderTextColor={COLORS.textMuted}
          secureTextEntry={secureToggle ? hidden : rest.secureTextEntry}
          onFocus={e => {
            setFocused(true);
            onFocus?.(e);
          }}
          onBlur={e => {
            setFocused(false);
            onBlur?.(e);
          }}
          accessibilityLabel={rest.accessibilityLabel ?? label}
        />
        {secureToggle ? (
          <TouchableOpacity onPress={() => setHidden(h => !h)} hitSlop={10} accessibilityRole="button">
            <Text style={styles.toggle}>{hidden ? 'SHOW' : 'HIDE'}</Text>
          </TouchableOpacity>
        ) : null}
      </View>
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </View>
  );
});

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
    paddingHorizontal: SPACING.md,
  },
  inputFocused: {
    borderColor: COLORS.primaryGlow,
  },
  inputError: {
    borderColor: COLORS.danger,
  },
  input: {
    flex: 1,
    color: COLORS.text,
    fontSize: 16,
    height: 48,
  },
  toggle: {
    color: COLORS.primaryGlow,
    fontSize: 12,
    fontWeight: '800',
    letterSpacing: 1,
    marginLeft: SPACING.sm,
  },
  error: {
    color: COLORS.danger,
    fontSize: 12,
    fontWeight: '600',
    marginTop: SPACING.xs,
  },
});
