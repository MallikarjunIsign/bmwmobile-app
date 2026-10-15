import React, { useEffect, useState } from 'react';
import { View, Text, StyleSheet, TextInput, TouchableOpacity, ScrollView, Keyboard } from 'react-native';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { MarketApi } from '../services/marketApi';
import type { ItemTypeFilter, QueryChip, Suggestion } from '../types/listing';

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

const GROUP_TITLES: Record<string, string> = {
  recent: 'Recent searches', categories: 'Categories', parts: 'Parts', fits: 'Parts for a vehicle', vehicles: 'Vehicles', partNumbers: 'Part numbers',
};
const GROUP_ORDER = ['categories', 'parts', 'fits', 'vehicles', 'partNumbers'];

interface SearchBoxProps {
  /** What is typed right now. */
  text: string;
  /** The active filter chips ([Bumper] [Toyota] [Lighting] …). */
  chips: QueryChip[];
  onChangeText: (text: string) => void;
  /** A suggestion or a recent search was tapped: apply its chips (the results update at once). */
  onPick: (chips: QueryChip[], text?: string) => void;
  onRemoveChip: (chip: QueryChip) => void;
  /** Keyboard "search": search the text with the active chips. */
  onSubmit: () => void;
  /** ✕: clear the text and the chips. */
  onClear: () => void;
  country?: string;
  /** Suggestions only for Parts or only for Vehicles (Home's first filter). */
  itemType?: ItemTypeFilter;
  placeholder?: string;
}

/**
 * One search box like an online shop: suggestions while typing (categories, parts, vehicles, part numbers, recent
 * searches); picking one turns it into filter chips ([Front bumper] [Toyota] [Hilux] [2016]); free text still works.
 * Every ScrollView around it needs keyboardShouldPersistTaps="handled" – otherwise the first tap on a suggestion
 * only closes the keyboard and is lost (that was the Home search bug).
 */
export const SearchBox: React.FC<SearchBoxProps> = ({
  text, chips, onChangeText, onPick, onRemoveChip, onSubmit, onClear, country, itemType, placeholder,
}) => {
  const [focused, setFocused] = useState(false);
  const [suggestions, setSuggestions] = useState<Suggestion[]>([]);
  const [recent, setRecent] = useState<SearchState[]>([]);

  useEffect(() => {
    if (focused) readRecent().then(setRecent);
  }, [focused]);

  // Suggestions, debounced; late answers are ignored.
  useEffect(() => {
    const q = text.trim();
    if (!focused || q.length < 2) {
      setSuggestions([]);
      return;
    }
    let alive = true;
    const timer = setTimeout(() => {
      MarketApi.suggest(q, country, itemType).then(list => alive && setSuggestions(list));
    }, SUGGEST_DEBOUNCE_MS);
    return () => {
      alive = false;
      clearTimeout(timer);
    };
  }, [text, focused, country, itemType]);

  const pick = (picked: QueryChip[], pickedText = '') => {
    setFocused(false);
    setSuggestions([]);
    Keyboard.dismiss();
    onPick(picked, pickedText);
  };

  const showRecent = focused && text.trim().length < 2 && recent.length > 0;
  const groups = GROUP_ORDER.map(g => ({ g, items: suggestions.filter(s => s.group === g) })).filter(x => x.items.length > 0);

  return (
    <View>
      <View style={[styles.bar, focused && styles.barFocused]}>
        <Text style={styles.icon}>🔍</Text>
        <ScrollView horizontal showsHorizontalScrollIndicator={false} keyboardShouldPersistTaps="handled" contentContainerStyle={styles.barContent}>
          {chips.map(chip => (
            <TouchableOpacity key={chip.type + chip.value} style={styles.chip} onPress={() => onRemoveChip(chip)}
              accessibilityLabel={`Remove ${chip.label}`} testID={`chip-${chip.type}`}>
              <Text style={styles.chipText} numberOfLines={1}>{chip.label} ✕</Text>
            </TouchableOpacity>
          ))}
          <TextInput
            style={styles.input}
            placeholder={chips.length ? 'Add words…' : placeholder ?? (itemType === 'Vehicle' ? 'Search vehicles: make, model, year…' : 'Search parts, part numbers, vehicles…')}
            placeholderTextColor={COLORS.textMuted}
            value={text}
            onChangeText={onChangeText}
            onFocus={() => setFocused(true)}
            onBlur={() => setTimeout(() => setFocused(false), 150)}
            returnKeyType="search"
            onSubmitEditing={() => {
              setFocused(false);
              onSubmit();
            }}
            accessibilityLabel="Search"
          />
        </ScrollView>
        {text || chips.length ? (
          <TouchableOpacity
            onPress={onClear}
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
                  <TouchableOpacity key={i} style={styles.row} onPress={() => pick(r.chips, r.text)} testID={`recent-${i}`}>
                    <Text style={styles.rowText}>🕘 {[...r.chips.map(c => c.label), r.text].filter(Boolean).join(' · ')}</Text>
                  </TouchableOpacity>
                ))}
              </View>
            ) : null}
            {groups.map(({ g, items }) => (
              <View key={g}>
                <Text style={styles.group}>{GROUP_TITLES[g].toUpperCase()}</Text>
                {items.map(s => (
                  <TouchableOpacity key={s.group + s.label} style={styles.row} onPress={() => pick(s.chips)}
                    testID={`suggestion-${s.group}-${s.label}`}>
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
