import React, { useState, useEffect } from 'react';
import { View, Text, StyleSheet, TextInput, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';

type Props = NativeStackScreenProps<RootStackParamList, 'Otp'>;

export const OtpScreen: React.FC<Props> = ({ route, navigation }) => {
  const { phoneNumber } = route.params || { phoneNumber: '+234 803 123 4567' };
  const [otp, setOtp] = useState('849201');
  const [timer, setTimer] = useState(45);

  useEffect(() => {
    const interval = setInterval(() => {
      setTimer(t => (t > 0 ? t - 1 : 0));
    }, 1000);
    return () => clearInterval(interval);
  }, []);

  const handleVerify = () => {
    navigation.navigate('ProfilePreferences');
  };

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Verify OTP" onBack={() => navigation.goBack()} />

      <View style={styles.content}>
        <Text style={styles.heading}>Enter 6-Digit Code</Text>
        <Text style={styles.subheading}>
          Sent to <Text style={styles.phoneHighlight}>{phoneNumber}</Text>
        </Text>

        <View style={styles.otpBox}>
          <TextInput
            style={styles.otpInput}
            value={otp}
            onChangeText={setOtp}
            keyboardType="number-pad"
            maxLength={6}
          />
        </View>

        <TouchableOpacity style={styles.verifyBtn} onPress={handleVerify} activeOpacity={0.8}>
          <Text style={styles.verifyText}>VERIFY & CONTINUE</Text>
        </TouchableOpacity>

        <View style={styles.resendRow}>
          <Text style={styles.resendLabel}>Didn't receive code? </Text>
          {timer > 0 ? (
            <Text style={styles.timerText}>Resend in {timer}s</Text>
          ) : (
            <TouchableOpacity onPress={() => setTimer(45)}>
              <Text style={styles.resendBtn}>Resend Code Now</Text>
            </TouchableOpacity>
          )}
        </View>
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
  },
  heading: {
    color: COLORS.text,
    fontSize: 22,
    fontWeight: '800',
    marginTop: SPACING.md,
  },
  subheading: {
    color: COLORS.textMuted,
    fontSize: 14,
    marginTop: SPACING.xs,
    marginBottom: SPACING.xl,
  },
  phoneHighlight: {
    color: COLORS.text,
    fontWeight: '700',
  },
  otpBox: {
    width: '100%',
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.lg,
    borderWidth: 1,
    borderColor: COLORS.primaryGlow,
    padding: SPACING.md,
    marginBottom: SPACING.xl,
  },
  otpInput: {
    color: COLORS.primaryGlow,
    fontSize: 32,
    fontWeight: '900',
    textAlign: 'center',
    letterSpacing: 12,
  },
  verifyBtn: {
    width: '100%',
    backgroundColor: COLORS.primary,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
  },
  verifyText: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '800',
    letterSpacing: 1,
  },
  resendRow: {
    flexDirection: 'row',
    marginTop: SPACING.xl,
  },
  resendLabel: {
    color: COLORS.textMuted,
    fontSize: 14,
  },
  timerText: {
    color: COLORS.textSecondary,
    fontSize: 14,
    fontWeight: '600',
  },
  resendBtn: {
    color: COLORS.primaryGlow,
    fontSize: 14,
    fontWeight: '700',
  },
});
