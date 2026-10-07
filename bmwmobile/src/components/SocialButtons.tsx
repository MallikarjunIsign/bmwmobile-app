import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { AppleButton } from '@invertase/react-native-apple-authentication';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { isAppleAvailable } from '../auth/social';
import { PrimaryButton } from './PrimaryButton';

interface SocialButtonsProps {
  onGoogle: () => void;
  onApple: () => void;
  busy?: boolean;
  label?: string;
}

/** "or continue with" + Google (Android and iPhone) + Apple (iPhone only, Apple's own button). */
export const SocialButtons: React.FC<SocialButtonsProps> = ({ onGoogle, onApple, busy, label = 'or continue with' }) => (
  <View style={styles.wrap}>
    <View style={styles.dividerRow}>
      <View style={styles.line} />
      <Text style={styles.dividerText}>{label.toUpperCase()}</Text>
      <View style={styles.line} />
    </View>
    <PrimaryButton
      variant="outline"
      label="Continue with Google"
      icon={<Text style={styles.googleG}>G</Text>}
      onPress={onGoogle}
      disabled={busy}
    />
    {isAppleAvailable() ? (
      <AppleButton
        buttonStyle={AppleButton.Style.WHITE}
        buttonType={AppleButton.Type.CONTINUE}
        cornerRadius={RADIUS.md}
        style={styles.apple}
        onPress={() => {
          if (!busy) onApple();
        }}
      />
    ) : null}
  </View>
);

const styles = StyleSheet.create({
  wrap: {
    gap: SPACING.md,
    marginTop: SPACING.lg,
  },
  dividerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: SPACING.md,
  },
  line: {
    flex: 1,
    height: 1,
    backgroundColor: COLORS.border,
  },
  dividerText: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1.5,
  },
  googleG: {
    color: COLORS.text,
    fontSize: 16,
    fontWeight: '900',
  },
  apple: {
    width: '100%',
    height: 50,
  },
});
