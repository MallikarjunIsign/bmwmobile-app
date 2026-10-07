import React, { useState } from 'react';
import { View, Text, TouchableOpacity, Modal, StyleSheet, ScrollView } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { useLocations } from '../location/LocationContext';
import { locationChipLabel, locationIcon } from '../utils/geo';

interface LocationSelectorProps {
  onManage: () => void;
}

/**
 * "📍 Home · Hyderabad · 25 km ▾" above the parts list. Tapping it picks which saved location
 * the list is filtered by (the choice is remembered on this phone).
 */
export const LocationSelector: React.FC<LocationSelectorProps> = ({ onManage }) => {
  const insets = useSafeAreaInsets();
  const { locations, selected, select } = useLocations();
  const [open, setOpen] = useState(false);

  if (!selected) return null;

  return (
    <View>
      <Text style={styles.header}>SHOWING PARTS NEAR</Text>
      <TouchableOpacity style={styles.chip} onPress={() => setOpen(true)} activeOpacity={0.8} accessibilityRole="button">
        <Text style={styles.chipIcon}>{locationIcon(selected)}</Text>
        <Text style={styles.chipText} numberOfLines={1}>{locationChipLabel(selected)}</Text>
        <Text style={styles.chevron}>▾</Text>
      </TouchableOpacity>

      <Modal visible={open} transparent animationType="slide" onRequestClose={() => setOpen(false)}>
        <TouchableOpacity style={styles.backdrop} activeOpacity={1} onPress={() => setOpen(false)}>
          <View style={[styles.sheet, { paddingBottom: insets.bottom + SPACING.lg }]}>
            <Text style={styles.sheetTitle}>CHOOSE LOCATION</Text>
            <ScrollView style={styles.list}>
              {locations.map(l => {
                const active = l.id === selected.id;
                return (
                  <TouchableOpacity
                    key={l.id}
                    style={[styles.row, active && styles.rowActive]}
                    onPress={() => {
                      select(l.id);
                      setOpen(false);
                    }}
                  >
                    <Text style={styles.rowIcon}>{locationIcon(l)}</Text>
                    <View style={styles.rowText}>
                      <Text style={styles.rowLabel}>
                        {l.label}
                        {l.isPrimary ? <Text style={styles.primaryTag}>  ★ PRIMARY</Text> : null}
                      </Text>
                      <Text style={styles.rowPlace}>{l.placeName} · {l.radiusKm} km</Text>
                    </View>
                    <Text style={[styles.radio, active && styles.radioActive]}>{active ? '●' : '○'}</Text>
                  </TouchableOpacity>
                );
              })}
            </ScrollView>
            <TouchableOpacity
              style={styles.manage}
              onPress={() => {
                setOpen(false);
                onManage();
              }}
            >
              <Text style={styles.manageText}>⚙️ Manage locations →</Text>
            </TouchableOpacity>
          </View>
        </TouchableOpacity>
      </Modal>
    </View>
  );
};

const styles = StyleSheet.create({
  header: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
    letterSpacing: 1,
    marginBottom: SPACING.xs,
  },
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'flex-start',
    gap: SPACING.sm,
    backgroundColor: COLORS.card,
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.sm,
    borderRadius: RADIUS.full,
    borderWidth: 1,
    borderColor: COLORS.primaryGlow,
    maxWidth: '100%',
  },
  chipIcon: {
    fontSize: 14,
  },
  chipText: {
    color: COLORS.text,
    fontSize: 13,
    fontWeight: '800',
    flexShrink: 1,
  },
  chevron: {
    color: COLORS.primaryGlow,
    fontSize: 12,
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
    maxHeight: '70%',
  },
  sheetTitle: {
    color: COLORS.textMuted,
    fontSize: 12,
    fontWeight: '800',
    letterSpacing: 1.5,
    paddingHorizontal: SPACING.lg,
    marginBottom: SPACING.sm,
  },
  list: {
    flexGrow: 0,
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
  rowIcon: {
    fontSize: 20,
  },
  rowText: {
    flex: 1,
  },
  rowLabel: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '800',
  },
  primaryTag: {
    color: COLORS.primaryGlow,
    fontSize: 11,
    fontWeight: '800',
  },
  rowPlace: {
    color: COLORS.textSecondary,
    fontSize: 13,
    marginTop: 2,
  },
  radio: {
    color: COLORS.textMuted,
    fontSize: 18,
  },
  radioActive: {
    color: COLORS.primaryGlow,
  },
  manage: {
    paddingHorizontal: SPACING.lg,
    paddingTop: SPACING.md,
    borderTopWidth: 1,
    borderTopColor: COLORS.border,
    marginTop: SPACING.sm,
  },
  manageText: {
    color: COLORS.primaryGlow,
    fontSize: 14,
    fontWeight: '800',
  },
});
