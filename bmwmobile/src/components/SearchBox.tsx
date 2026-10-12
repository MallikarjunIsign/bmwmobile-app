import React, { useEffect, useState } from 'react';
import { View, Text, StyleSheet, TextInput, TouchableOpacity, ScrollView, Keyboard } from 'react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { MarketApi } from '../services/marketApi';
import type { QueryChip, Suggestion } from '../types/listing';

const RECENT_KEY = '@bidmyparts/recent-searches';
const MAX_RECENT = 8;
export const SUGGEST_DEBOUNCE_MS = 250;

export interface SearchState {
  text: string;
  chips: QueryChip[];
}

async function readRecent(): Promise<SearchState[]> {
  try {
    return JSON.parse((await AsyncStorage.getItem(RECENT_KEY)) ?? '[]');
  } catch {
    return [];
  }
}

/** Remembers a search for "Recent searches". */
export async function rememberSearch(search: SearchState) {
  if (!search.text.trim() && search.chips.length === 0) return;
  const key = (s: SearchState) => `${s.text.trim().toLowerCase()}|${s.chips.map(c => c.type + c.value).join(',')}`;
  const list = [search, ...(await readRecent()).filter(s => key(s) !== key(search))].slice(0, MAX_RECENT);
  await AsyncStorage.setItem(RECENT_KEY, JSON.stringify(list)).catch(() => undefined);
}

const GROUP_TITLES: Record<string, string> = { recent: 'Recent searches', parts: 'Parts', vehicles: 'Vehicles', partNumbers: 'Part numbers' };

interface SearchBoxProps {
  value: SearchState;
  onChange: (value: SearchState) => void;
  /** Search now (keyboard "search" or a suggestion). */
  onSubmit: (value: SearchState) => void;
  country?: string;
  placeholder?: string;
}

/**
 * One search box like an online shop: suggestions while typing (parts, vehicles, part numbers, recent searches);
 * picking one turns it into filter chips ([Front bumper] [Toyota] [Hilux] [2016]); free text still works.
 */
export const SearchBox: React.FC<SearchBoxProps> = ({ value, onChange, onSubmit, country, placeholder }) => {
  const [focused, setFocused] = useState(false);
  const [suggestions, setSuggestions] = useState<Suggestion[]>([]);
  const [recent, setRecent] = useState<SearchState[]>([]);

  useEffect(() => {
    if (focused) readRecent().then(setRecent);
  }, [focused]);

  // Suggestions, debounced; late answers are ignored.
  useEffect(() => {
    const q = value.text.trim();
    if (!focused || q.length < 2) {
      setSuggestions([]);
      return;
    }
    let alive = true;
    const timer = setTimeout(() => {
      MarketApi.suggest(q, country).then(list => alive && setSuggestions(list));
    }, SUGGEST_DEBOUNCE_MS);
    return () => {
      alive = false;
      clearTimeout(timer);
    };
  }, [value.text, focused, country]);

  const pick = (chips: QueryChip[], text = '') => {
    const merged = [...value.chips.filter(c => !chips.some(n => n.type === c.type)), ...chips];
    const next = { text, chips: merged };
    onChange(next);
    onSubmit(next);
    setFocused(false);
    Keyboard.dismiss();
  };

  const removeChip = (chip: QueryChip) => {
    const next = { ...value, chips: value.chips.filter(c => c !== chip) };
    onChange(next);
    onSubmit(next);
  };

  const showRecent = focused && value.text.trim().length < 2 && recent.length > 0;
  const groups = ['parts', 'vehicles', 'partNumbers'].map(g => ({ g, items: suggestions.filter(s => s.group === g) })).filter(x => x.items.length > 0);

  return (
    <View>
      <View style={[styles.bar, focused && styles.barFocused]}>
        <Text style={styles.icon}>🔍</Text>
        <ScrollView horizontal showsHorizontalScrollIndicator={false} keyboardShouldPersistTaps="handled" contentContainerStyle={styles.barContent}>
          {value.chips.map(chip => (
            <TouchableOpacity key={chip.type + chip.value} style={styles.chip} onPress={() => removeChip(chip)} accessibilityLabel={`Remove ${chip.label}`}>
              <Text style={styles.chipText} numberOfLines={1}>{chip.label} ✕</Text>
            </TouchableOpacity>
          ))}
          <TextInput
            style={styles.input}
            placeholder={value.chips.length ? 'Add words…' : placeholder ?? 'Search parts, part numbers, vehicles…'}
            placeholderTextColor={COLORS.textMuted}
            value={value.text}
            onChangeText={text => onChange({ ...value, text })}
            onFocus={() => setFocused(true)}
            onBlur={() => setTimeout(() => setFocused(false), 150)}
            returnKeyType="search"
            onSubmitEditing={() => {
              setFocused(false);
              onSubmit(value);
            }}
            accessibilityLabel="Search"
          />
        </ScrollView>
        {value.text || value.chips.length ? (
          <TouchableOpacity
            onPress={() => {
              const empty = { text: '', chips: [] };
              onChange(empty);
              onSubmit(empty);
            }}
            hitSlop={8}
            accessibilityLabel="Clear search"
          >
            <Text style={styles.clear}>✕</Text>
          </TouchableOpacity>
        ) : null}
      </View>

      {focused && (groups.length > 0 || showRecent) ? (
        <View style={styles.dropdown}>
          <ScrollView keyboardShouldPersistTaps="handled" style={styles.dropdownScroll}>
            {showRecent ? (
              <View>
                <Text style={styles.group}>{GROUP_TITLES.recent.toUpperCase()}</Text>
                {recent.map((r, i) => (
                  <TouchableOpacity key={i} style={styles.row} onPress={() => pick(r.chips, r.text)}>
                    <Text style={styles.rowText}>🕘 {[...r.chips.map(c => c.label), r.text].filter(Boolean).join(' · ')}</Text>
                  </TouchableOpacity>
                ))}
              </View>
            ) : null}
            {groups.map(({ g, items }) => (
              <View key={g}>
                <Text style={styles.group}>{GROUP_TITLES[g].toUpperCase()}</Text>
                {items.map(s => (
                  <TouchableOpacity key={s.group + s.label} style={styles.row} onPress={() => pick(s.chips)}>
                    <Text style={styles.rowText}>{s.label}</Text>
                    {s.subtitle ? <Text style={styles.rowSub}>{s.subtitle}</Text> : null}
                  </TouchableOpacity>
                ))}
              </View>
            ))}
          </ScrollView>
        </View>
      ) : null}
    </View>
  );
};

const styles = StyleSheet.create({
  bar: {
    flexDirection: 'row', alignItems: 'center', backgroundColor: COLORS.card, borderRadius: RADIUS.md,
    paddingHorizontal: SPACING.md, borderWidth: 1, borderColor: COLORS.borderHighlight, minHeight: 46,
  },
  barFocused: { borderColor: COLORS.primaryGlow },
  barContent: { alignItems: 'center', flexGrow: 1 },
  icon: { fontSize: 16, marginRight: SPACING.sm },
  input: { color: COLORS.text, fontSize: 14, minWidth: 160, flexGrow: 1, paddingVertical: 8 },
  chip: {
    backgroundColor: COLORS.badgeBg, borderColor: COLORS.primary, borderWidth: 1, borderRadius: RADIUS.full,
    paddingHorizontal: SPACING.sm, paddingVertical: 3, marginRight: SPACING.xs, maxWidth: 160,
  },
  chipText: { color: COLORS.badgeText, fontSize: 12, fontWeight: '700' },
  clear: { color: COLORS.textMuted, fontSize: 16, padding: 4 },
  dropdown: {
    marginTop: 4, backgroundColor: COLORS.surface, borderRadius: RADIUS.md, borderWidth: 1, borderColor: COLORS.borderHighlight,
  },
  dropdownScroll: { maxHeight: 320 },
  group: { color: COLORS.textMuted, fontSize: 10, fontWeight: '800', letterSpacing: 1, paddingHorizontal: SPACING.md, paddingTop: SPACING.sm },
  row: { paddingHorizontal: SPACING.md, paddingVertical: SPACING.sm, borderBottomWidth: 1, borderBottomColor: COLORS.border },
  rowText: { color: COLORS.text, fontSize: 14, fontWeight: '600' },
  rowSub: { color: COLORS.textMuted, fontSize: 11, marginTop: 2 },
});
