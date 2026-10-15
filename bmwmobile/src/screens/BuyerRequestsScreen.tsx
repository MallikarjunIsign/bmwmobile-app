import React, { useCallback, useState } from 'react';
import { View, Text, StyleSheet, FlatList, TouchableOpacity, RefreshControl } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect } from '@react-navigation/native';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { MarketApi } from '../services/marketApi';
import { useMoney } from '../location/useMoney';
import { formatWhen } from '../utils/time';
import type { OpenPartRequest } from '../types/listing';

type Props = NativeStackScreenProps<RootStackParamList, 'BuyerRequests'>;

/**
 * Sellers: what buyers are looking for (open part requests, newest first). Each request shows the part, the vehicle,
 * the buyer's city and budget – never who they are. "I have this – list it" opens Sell; when the listing matches,
 * the buyer is told automatically.
 */
export const BuyerRequestsScreen: React.FC<Props> = ({ navigation }) => {
  const money = useMoney();
  const [requests, setRequests] = useState<OpenPartRequest[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setRequests(await MarketApi.openPartRequests());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Couldn't load buyer requests");
    } finally {
      setLoading(false);
    }
  }, []);

  useFocusEffect(useCallback(() => { load(); }, [load]));

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Buyer requests" subtitle="Parts buyers are looking for" onBack={() => navigation.goBack()} />
      {error ? <Text style={styles.error}>{error}</Text> : null}
      <FlatList
        data={requests}
        keyExtractor={r => r.id}
        contentContainerStyle={styles.list}
        refreshControl={<RefreshControl refreshing={loading} onRefresh={load} tintColor={COLORS.primaryGlow} />}
        ListEmptyComponent={loading || error ? undefined : (
          <View style={styles.card}>
            <Text style={styles.meta}>No open requests right now. You'll get a notification when a buyer asks for a part.</Text>
          </View>
        )}
        renderItem={({ item }) => (
          <View style={styles.card}>
            <Text style={styles.title}>🔎 {item.title}</Text>
            {item.partNumber ? <Text style={styles.meta}>Part no. {item.partNumber}</Text> : null}
            {item.specs ? <Text style={styles.meta}>{item.specs}</Text> : null}
            <Text style={styles.meta}>
              {[item.locationName ? `📍 ${item.locationName}` : null, item.maxBudget ? `Budget up to ${money(item.maxBudget)}` : null, formatWhen(item.createdAt)]
                .filter(Boolean).join(' · ')}
            </Text>
            <TouchableOpacity style={styles.btn} onPress={() => navigation.navigate('MainTabs', { screen: 'Sell' })} activeOpacity={0.8}>
              <Text style={styles.btnText}>I have this – list it</Text>
            </TouchableOpacity>
          </View>
        )}
      />
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  list: { padding: SPACING.md, paddingBottom: SPACING.xxl },
  card: { backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, padding: SPACING.md, borderWidth: 1, borderColor: COLORS.border, marginBottom: SPACING.md, gap: 4 },
  title: { color: COLORS.text, fontSize: 15, fontWeight: '800' },
  meta: { color: COLORS.textSecondary, fontSize: 12 },
  btn: { backgroundColor: COLORS.primary, paddingVertical: 10, borderRadius: RADIUS.md, alignItems: 'center', marginTop: SPACING.sm },
  btnText: { color: COLORS.text, fontWeight: '800', fontSize: 13 },
  error: { color: COLORS.danger, paddingHorizontal: SPACING.md, paddingTop: SPACING.sm, fontSize: 13 },
});
