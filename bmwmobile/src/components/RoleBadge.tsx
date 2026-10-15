import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { COLORS, RADIUS } from '../theme/colors';
import { normalizeRole } from '../utils/roles';

/**
 * Solid fills with a contrasting text colour (about 5:1 or more), so the pill reads on light and dark backgrounds:
 * seller green, buyer purple, admin amber – all from the app theme.
 */
const ROLE_STYLE = {
  seller: { bg: COLORS.successGlow, text: COLORS.background, label: 'SELLER' },
  buyer: { bg: COLORS.primary, text: COLORS.text, label: 'BUYER' },
  admin: { bg: COLORS.warningGlow, text: COLORS.background, label: 'ADMIN' },
} as const;

/** [SELLER] / [BUYER] / [ADMIN] – the signed-in user's role (from the token; updates on the next token refresh). */
export const RoleBadge: React.FC<{ role?: string | null }> = ({ role }) => {
  const r = ROLE_STYLE[normalizeRole(role)];
  return (
    <View style={[styles.pill, { backgroundColor: r.bg }]} accessibilityLabel={`Signed in as ${r.label.toLowerCase()}`} testID="role-badge">
      <Text style={[styles.text, { color: r.text }]} numberOfLines={1}>{r.label}</Text>
    </View>
  );
};

const styles = StyleSheet.create({
  // Never shrinks: a long location is shortened instead.
  pill: { borderRadius: RADIUS.full, paddingHorizontal: 7, paddingVertical: 1, marginRight: 6, flexShrink: 0 },
  text: { fontSize: 10, fontWeight: '900', letterSpacing: 0.8 },
});
