import React, { useEffect, useMemo, useState } from 'react';
import { View, Text, StyleSheet, TouchableOpacity, Modal, ScrollView } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { COLORS, SPACING, RADIUS } from '../../theme/colors';
import { countdown } from '../../utils/time';

/** Same limits as the server: an auction runs at least 1 hour and at most 30 days. */
export const MIN_AUCTION_MS = 3600 * 1000;
export const MAX_AUCTION_DAYS = 30;

const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const MINUTES = [0, 15, 30, 45];

const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate());

/** "Mon 14 Oct, 6:30 pm" in the phone's time zone. */
export function formatEnd(date: Date): string {
  const h = date.getHours();
  return `${DAYS[date.getDay()]} ${date.getDate()} ${MONTHS[date.getMonth()]}, ${h % 12 || 12}:${String(date.getMinutes()).padStart(2, '0')} ${h < 12 ? 'am' : 'pm'}`;
}

/** Why this end can't be used (null = fine). */
export function endProblem(end: Date, now = Date.now()): string | null {
  if (end.getTime() < now + MIN_AUCTION_MS) return 'Choose a time at least 1 hour from now';
  if (end.getTime() > now + MAX_AUCTION_DAYS * 86400000) return `An auction can run for at most ${MAX_AUCTION_DAYS} days`;
  return null;
}

const dayLabel = (day: Date, today: Date) => {
  const diff = Math.round((day.getTime() - today.getTime()) / 86400000);
  if (diff === 0) return 'Today';
  if (diff === 1) return 'Tomorrow';
  return `${DAYS[day.getDay()]} ${day.getDate()} ${MONTHS[day.getMonth()]}`;
};

/** The seller picks the day, hour and minute the auction ends (their local time). */
export const EndTimePicker: React.FC<{
  visible: boolean;
  value: Date;
  onClose: () => void;
  onPick: (end: Date) => void;
}> = ({ visible, value, onClose, onPick }) => {
  const insets = useSafeAreaInsets();
  const [draft, setDraft] = useState(value);

  useEffect(() => {
    if (visible) setDraft(value);
  }, [visible]); // eslint-disable-line react-hooks/exhaustive-deps

  const today = useMemo(() => startOfDay(new Date()), [visible]); // eslint-disable-line react-hooks/exhaustive-deps
  const days = useMemo(
    () => Array.from({ length: MAX_AUCTION_DAYS + 1 }, (_, i) => new Date(today.getFullYear(), today.getMonth(), today.getDate() + i)),
    [today],
  );

  const set = (patch: { day?: Date; hour?: number; minute?: number }) => {
    const day = patch.day ?? startOfDay(draft);
    setDraft(new Date(day.getFullYear(), day.getMonth(), day.getDate(), patch.hour ?? draft.getHours(), patch.minute ?? draft.getMinutes()));
  };

  const preset = (daysAhead: number) => {
    const d = new Date(Date.now() + daysAhead * 86400000);
    // Round up to the next quarter hour.
    d.setSeconds(0, 0);
    d.setMinutes(Math.ceil(d.getMinutes() / 15) * 15);
    setDraft(d);
  };

  const problem = endProblem(draft);
  const selectedDay = startOfDay(draft).getTime();

  return (
    <Modal visible={visible} animationType="slide" transparent onRequestClose={onClose}>
      <View style={styles.backdrop}>
        <View style={[styles.sheet, { paddingBottom: insets.bottom + SPACING.md }]}>
          <View style={styles.head}>
            <Text style={styles.title}>When does the auction end?</Text>
            <TouchableOpacity onPress={onClose} hitSlop={10}>
              <Text style={styles.close}>✕</Text>
            </TouchableOpacity>
          </View>

          <Text style={styles.label}>QUICK PICK</Text>
          <View style={styles.row}>
            {[1, 3, 5, 7].map(n => (
              <TouchableOpacity key={n} style={styles.chip} onPress={() => preset(n)}>
                <Text style={styles.chipText}>In {n} {n === 1 ? 'day' : 'days'}</Text>
              </TouchableOpacity>
            ))}
          </View>

          <Text style={styles.label}>DAY</Text>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.row}>
            {days.map(d => {
              const active = d.getTime() === selectedDay;
              return (
                <TouchableOpacity key={d.getTime()} style={[styles.chip, active && styles.chipActive]} onPress={() => set({ day: d })}
                  accessibilityState={{ selected: active }}>
                  <Text style={[styles.chipText, active && styles.chipTextActive]}>{dayLabel(d, today)}</Text>
                </TouchableOpacity>
              );
            })}
          </ScrollView>

          <Text style={styles.label}>HOUR</Text>
          <View style={styles.wrap}>
            {Array.from({ length: 24 }, (_, h) => {
              const active = draft.getHours() === h;
              return (
                <TouchableOpacity key={h} style={[styles.small, active && styles.chipActive]} onPress={() => set({ hour: h })}
                  accessibilityState={{ selected: active }}>
                  <Text style={[styles.chipText, active && styles.chipTextActive]}>{`${h % 12 || 12}${h < 12 ? 'am' : 'pm'}`}</Text>
                </TouchableOpacity>
              );
            })}
          </View>

          <Text style={styles.label}>MINUTE</Text>
          <View style={styles.row}>
            {MINUTES.map(m => {
              const active = draft.getMinutes() === m;
              return (
                <TouchableOpacity key={m} style={[styles.chip, active && styles.chipActive]} onPress={() => set({ minute: m })}
                  accessibilityState={{ selected: active }}>
                  <Text style={[styles.chipText, active && styles.chipTextActive]}>:{String(m).padStart(2, '0')}</Text>
                </TouchableOpacity>
              );
            })}
          </View>

          <View style={styles.summary}>
            <Text style={styles.summaryText}>Ends {formatEnd(draft)}</Text>
            {problem ? (
              <Text style={styles.error}>{problem}</Text>
            ) : (
              <Text style={styles.hint}>{countdown(draft.toISOString())} from now</Text>
            )}
          </View>

          <TouchableOpacity style={[styles.done, problem && styles.doneDisabled]} disabled={!!problem} onPress={() => onPick(draft)}>
            <Text style={styles.doneText}>Set end time</Text>
          </TouchableOpacity>
        </View>
      </View>
    </Modal>
  );
};

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.6)', justifyContent: 'flex-end' },
  sheet: { backgroundColor: COLORS.background, borderTopLeftRadius: RADIUS.lg, borderTopRightRadius: RADIUS.lg, padding: SPACING.md },
  head: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  title: { color: COLORS.text, fontSize: 17, fontWeight: '800' },
  close: { color: COLORS.textMuted, fontSize: 20 },
  label: { color: COLORS.textMuted, fontSize: 11, fontWeight: '800', letterSpacing: 1, marginTop: SPACING.md, marginBottom: SPACING.xs },
  row: { flexDirection: 'row', gap: SPACING.xs },
  wrap: { flexDirection: 'row', flexWrap: 'wrap', gap: SPACING.xs },
  chip: {
    backgroundColor: COLORS.card, paddingHorizontal: SPACING.md, paddingVertical: SPACING.xs + 4, borderRadius: RADIUS.full,
    borderWidth: 1, borderColor: COLORS.border,
  },
  small: {
    backgroundColor: COLORS.card, width: 52, alignItems: 'center', paddingVertical: SPACING.xs + 2, borderRadius: RADIUS.md,
    borderWidth: 1, borderColor: COLORS.border,
  },
  chipActive: { backgroundColor: COLORS.primary, borderColor: COLORS.primaryGlow },
  chipText: { color: COLORS.textSecondary, fontSize: 13, fontWeight: '600' },
  chipTextActive: { color: COLORS.text, fontWeight: '800' },
  summary: { marginTop: SPACING.md, padding: SPACING.md, backgroundColor: COLORS.surface, borderRadius: RADIUS.md, borderWidth: 1, borderColor: COLORS.border },
  summaryText: { color: COLORS.text, fontSize: 15, fontWeight: '800' },
  hint: { color: COLORS.textMuted, fontSize: 12, marginTop: 4 },
  error: { color: COLORS.danger, fontSize: 12, fontWeight: '700', marginTop: 4 },
  done: { marginTop: SPACING.md, backgroundColor: COLORS.primary, paddingVertical: 14, borderRadius: RADIUS.md, alignItems: 'center' },
  doneDisabled: { opacity: 0.4 },
  doneText: { color: COLORS.text, fontWeight: '800', fontSize: 14 },
});
