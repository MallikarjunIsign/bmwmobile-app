import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';

type Props = NativeStackScreenProps<RootStackParamList, 'PartUnavailable'>;

export const PartUnavailableScreen: React.FC<Props> = ({ route, navigation }) => {
  const { auctionTitle, make, model } = route.params;

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Part Unavailable" onBack={() => navigation.goBack()} />

      <View style={styles.content}>
        <Text style={styles.icon}>🚫</Text>
        <Text style={styles.title}>This BMW Auction Has Ended</Text>
        <Text style={styles.desc}>
          "{auctionTitle || 'Requested BMW Part'}" has already been sold or expired from live bidding.
        </Text>

        <View style={styles.box}>
          <Text style={styles.boxTitle}>✨ Order it via Custom Container Packing</Text>
          <Text style={styles.boxSub}>
            Our Munich & Bremerhaven sourcing agents will find this exact part and load it directly into the next scheduled Nigerian container.
          </Text>
        </View>

        <TouchableOpacity 
          style={styles.btn}
          onPress={() => navigation.navigate('OrderPartRequest', { prefillMake: make, prefillModel: model })}
          activeOpacity={0.8}
        >
          <Text style={styles.btnText}>SUBMIT CUSTOM PART REQUEST NOW →</Text>
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
  icon: {
    fontSize: 50,
    marginBottom: SPACING.md,
  },
  title: {
    color: COLORS.text,
    fontSize: 20,
    fontWeight: '800',
    textAlign: 'center',
  },
  desc: {
    color: COLORS.textMuted,
    fontSize: 14,
    textAlign: 'center',
    marginTop: SPACING.xs,
    marginBottom: SPACING.xl,
  },
  box: {
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.primaryGlow,
    marginBottom: SPACING.xl,
  },
  boxTitle: {
    color: COLORS.primaryGlow,
    fontWeight: '800',
    fontSize: 14,
    marginBottom: 4,
  },
  boxSub: {
    color: COLORS.textSecondary,
    fontSize: 12,
    lineHeight: 18,
  },
  btn: {
    width: '100%',
    backgroundColor: COLORS.primary,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  btnText: {
    color: COLORS.text,
    fontWeight: '900',
    fontSize: 13,
  },
});
