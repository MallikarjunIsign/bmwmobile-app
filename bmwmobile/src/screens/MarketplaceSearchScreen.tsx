import React, { useState, useEffect } from 'react';
import { View, Text, StyleSheet, TextInput, ScrollView, TouchableOpacity, FlatList } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { CompositeScreenProps } from '@react-navigation/native';
import { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { MainTabParamList, RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { ApiService } from '../services/api';
import { Auction } from '../types/models';
import { AuctionCard } from '../components/AuctionCard';

type Props = CompositeScreenProps<
  BottomTabScreenProps<MainTabParamList, 'Marketplace'>,
  NativeStackScreenProps<RootStackParamList>
>;

const CATEGORIES = ['All', 'Engines', 'Headlights', 'Bumpers', 'Transmission', 'Suspension'];
const BMW_MODELS = ['All Models', 'M4 Competition', '330i G20', 'X5 xDrive', 'M3 CS'];

export const MarketplaceSearchScreen: React.FC<Props> = ({ navigation }) => {
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedCategory, setSelectedCategory] = useState('All');
  const [selectedModel, setSelectedModel] = useState('All Models');
  const [auctions, setAuctions] = useState<Auction[]>([]);
  const [loading, setLoading] = useState(false);

  const fetchResults = async () => {
    setLoading(true);
    const data = await ApiService.getAuctions(searchTerm);
    setAuctions(data);
    setLoading(false);
  };

  useEffect(() => {
    fetchResults();
  }, [searchTerm, selectedCategory, selectedModel]);

  return (
    <SafeAreaView style={styles.container}>
      {/* Search Header Bar */}
      <View style={styles.searchHeader}>
        <View style={styles.searchBar}>
          <Text style={styles.searchIcon}>🔍</Text>
          <TextInput
            style={styles.searchInput}
            placeholder="Search BMW part, S58, LaserLight, VIN..."
            placeholderTextColor={COLORS.textMuted}
            value={searchTerm}
            onChangeText={setSearchTerm}
          />
          {searchTerm ? (
            <TouchableOpacity onPress={() => setSearchTerm('')}>
              <Text style={styles.clearIcon}>✕</Text>
            </TouchableOpacity>
          ) : null}
        </View>
      </View>

      {/* Category Pills */}
      <View style={styles.pillsWrap}>
        <ScrollView horizontal showsHorizontalScrollIndicator={false}>
          {CATEGORIES.map(cat => (
            <TouchableOpacity
              key={cat}
              style={[styles.pill, selectedCategory === cat && styles.pillActive]}
              onPress={() => setSelectedCategory(cat)}
            >
              <Text style={[styles.pillText, selectedCategory === cat && styles.pillTextActive]}>
                {cat}
              </Text>
            </TouchableOpacity>
          ))}
        </ScrollView>
      </View>

      {/* Model Pills */}
      <View style={styles.modelsWrap}>
        <ScrollView horizontal showsHorizontalScrollIndicator={false}>
          {BMW_MODELS.map(mod => (
            <TouchableOpacity
              key={mod}
              style={[styles.modelPill, selectedModel === mod && styles.modelPillActive]}
              onPress={() => setSelectedModel(mod)}
            >
              <Text style={[styles.modelText, selectedModel === mod && styles.modelTextActive]}>
                {mod}
              </Text>
            </TouchableOpacity>
          ))}
        </ScrollView>
      </View>

      {/* Results Header */}
      <View style={styles.resultsHeader}>
        <Text style={styles.resultsCount}>{auctions.length} BMW Part Auctions Found</Text>
        <Text style={styles.sortLabel}>Sort: Ending Soonest ▾</Text>
      </View>

      {/* Auction List */}
      <FlatList
        data={auctions}
        keyExtractor={item => item.id}
        contentContainerStyle={styles.listContent}
        renderItem={({ item }) => (
          <AuctionCard
            auction={item}
            onPress={() => navigation.navigate('PartDetails', { auctionId: item.id, auction: item })}
            onBidPress={() => navigation.navigate('PlaceBidModal', { auction: item })}
          />
        )}
        ListEmptyComponent={
          <View style={styles.empty}>
            <Text style={styles.emptyIcon}>🔍</Text>
            <Text style={styles.emptyTitle}>No Matching Auctions</Text>
            <Text style={styles.emptyDesc}>We couldn't find active auctions matching "{searchTerm}".</Text>
            <TouchableOpacity 
              style={styles.requestRedirectBtn}
              onPress={() => navigation.navigate('OrderPartRequest', { prefillPart: searchTerm })}
            >
              <Text style={styles.requestRedirectText}>Submit a Custom Part Request →</Text>
            </TouchableOpacity>
          </View>
        }
      />
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.background,
  },
  searchHeader: {
    padding: SPACING.md,
    backgroundColor: COLORS.surface,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.border,
  },
  searchBar: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: COLORS.card,
    borderRadius: RADIUS.md,
    paddingHorizontal: SPACING.md,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
    height: 44,
  },
  searchIcon: {
    fontSize: 16,
    marginRight: SPACING.sm,
  },
  searchInput: {
    flex: 1,
    color: COLORS.text,
    fontSize: 14,
  },
  clearIcon: {
    color: COLORS.textMuted,
    fontSize: 16,
    padding: 4,
  },
  pillsWrap: {
    backgroundColor: COLORS.surface,
    paddingVertical: SPACING.xs + 2,
    paddingHorizontal: SPACING.md,
  },
  pill: {
    backgroundColor: COLORS.card,
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.xs,
    borderRadius: RADIUS.full,
    marginRight: SPACING.xs,
    borderWidth: 1,
    borderColor: COLORS.border,
  },
  pillActive: {
    backgroundColor: COLORS.primary,
    borderColor: COLORS.primaryGlow,
  },
  pillText: {
    color: COLORS.textSecondary,
    fontSize: 12,
    fontWeight: '600',
  },
  pillTextActive: {
    color: COLORS.text,
    fontWeight: '700',
  },
  modelsWrap: {
    backgroundColor: COLORS.background,
    paddingVertical: SPACING.xs + 2,
    paddingHorizontal: SPACING.md,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.border,
  },
  modelPill: {
    backgroundColor: COLORS.cardSecondary,
    paddingHorizontal: SPACING.sm + 4,
    paddingVertical: 4,
    borderRadius: RADIUS.sm,
    marginRight: SPACING.xs,
  },
  modelPillActive: {
    backgroundColor: COLORS.badgeBg,
    borderWidth: 1,
    borderColor: COLORS.primary,
  },
  modelText: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '600',
  },
  modelTextActive: {
    color: COLORS.primaryGlow,
    fontWeight: '700',
  },
  resultsHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingHorizontal: SPACING.md,
    paddingVertical: SPACING.sm,
  },
  resultsCount: {
    color: COLORS.textSecondary,
    fontSize: 12,
    fontWeight: '700',
  },
  sortLabel: {
    color: COLORS.primaryGlow,
    fontSize: 12,
    fontWeight: '600',
  },
  listContent: {
    padding: SPACING.md,
    paddingBottom: SPACING.xxl,
  },
  empty: {
    alignItems: 'center',
    paddingVertical: SPACING.xxl,
  },
  emptyIcon: {
    fontSize: 40,
    marginBottom: SPACING.md,
  },
  emptyTitle: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '800',
  },
  emptyDesc: {
    color: COLORS.textMuted,
    fontSize: 13,
    textAlign: 'center',
    marginTop: SPACING.xs,
    marginBottom: SPACING.lg,
  },
  requestRedirectBtn: {
    backgroundColor: COLORS.primary,
    paddingHorizontal: SPACING.lg,
    paddingVertical: SPACING.md,
    borderRadius: RADIUS.md,
  },
  requestRedirectText: {
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '800',
  },
});
