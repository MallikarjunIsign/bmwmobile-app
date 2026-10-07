import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Badge } from '../components/Badge';

type Props = NativeStackScreenProps<RootStackParamList, 'EndingSoon'>;

export const EndingSoonScreen: React.FC<Props> = ({ route, navigation }) => {
  const { auction } = route.params;

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.content}>
        <View style={styles.iconCircle}>
          <Text style={styles.iconText}>⏳</Text>
        </View>

        <Badge label="🔥 AUCTION ENDING IN 14 MINS" variant="warning" />

        <Text style={styles.title}>{auction.title}</Text>

        <View style={styles.card}>
          <Text style={styles.label}>LIVE COUNTDOWN</Text>
          <Text style={styles.timer}>14m : 28s</Text>
          <Text style={styles.currentBid}>Current High Bid: ${auction.currentHighBid.toLocaleString()}</Text>
        </View>

        <TouchableOpacity 
          style={styles.bidBtn}
          onPress={() => navigation.navigate('PlaceBidModal', { auction })}
        >
          <Text style={styles.bidText}>PLACE LAST-MINUTE BID (${(auction.currentHighBid + 250).toLocaleString()})</Text>
        </TouchableOpacity>
      </View>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.background,
  },
  content: {
    padding: SPACING.lg,
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
  },
  iconCircle: {
    width: 80,
    height: 80,
    borderRadius: 40,
    backgroundColor: 'rgba(245, 176, 20, 0.15)',
    justifyContent: 'center',
    alignItems: 'center',
    borderWidth: 2,
    borderColor: COLORS.warning,
    marginBottom: SPACING.md,
  },
  iconText: {
    fontSize: 40,
  },
  title: {
    color: COLORS.text,
    fontSize: 18,
    fontWeight: '800',
    textAlign: 'center',
    marginVertical: SPACING.md,
  },
  card: {
    width: '100%',
    backgroundColor: COLORS.surface,
    padding: SPACING.lg,
    borderRadius: RADIUS.lg,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: COLORS.warning,
    marginBottom: SPACING.xl,
  },
  label: {
    color: COLORS.textMuted,
    fontSize: 11,
    fontWeight: '800',
  },
  timer: {
    color: COLORS.warningGlow,
    fontSize: 36,
    fontWeight: '900',
    marginVertical: SPACING.xs,
  },
  currentBid: {
    color: COLORS.textSecondary,
    fontSize: 14,
    fontWeight: '700',
  },
  bidBtn: {
    width: '100%',
    backgroundColor: COLORS.primary,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  bidText: {
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '900',
  },
});
