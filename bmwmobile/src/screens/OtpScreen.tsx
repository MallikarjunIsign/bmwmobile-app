import React, { useState } from 'react';
import { View, Text, StyleSheet, TouchableOpacity, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING } from '../theme/colors';
import { Header } from '../components/Header';
import { OtpCodeInput } from '../components/OtpCodeInput';
import { CheckboxRow } from '../components/CheckboxRow';
import { Notice } from '../components/Notice';
import { PrimaryButton } from '../components/PrimaryButton';
import { useAuth } from '../auth/AuthContext';
import { useCountdown } from '../hooks/useCountdown';
import { errorMessage } from '../services/authApi';

type Props = NativeStackScreenProps<RootStackParamList, 'Otp'>;

export const OtpScreen: React.FC<Props> = ({ route, navigation }) => {
  const { challengeId, maskedEmail, resendAfterSeconds, rememberMe } = route.params;
  const { verifyOtp, resendOtp } = useAuth();
  const timer = useCountdown(resendAfterSeconds);

  const [otp, setOtp] = useState('');
  const [rememberDevice, setRememberDevice] = useState(true);
  const [devOtp, setDevOtp] = useState(route.params.devOtp ?? null);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [verifying, setVerifying] = useState(false);
  const [resending, setResending] = useState(false);

  const handleVerify = async (code = otp) => {
    setInfo(null);
    if (code.length < 6) {
      setError('Enter the 6-digit code');
      return;
    }
    setError(null);
    setVerifying(true);
    try {
      await verifyOtp(challengeId, code, rememberDevice, rememberMe);
      // Signed in: the navigator switches to the marketplace by itself.
    } catch (err) {
      setError(errorMessage(err, 'Verification failed. Please try again.'));
      setOtp('');
    } finally {
      setVerifying(false);
    }
  };

  const handleResend = async () => {
    if (timer.seconds > 0 || resending) return;
    setError(null);
    setInfo(null);
    setResending(true);
    try {
      const res = await resendOtp(challengeId);
      setOtp('');
      setDevOtp(res.devOtp ?? null);
      setInfo('New code sent. Check your email.');
      timer.restart(res.resendAfterSeconds);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setResending(false);
    }
  };

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Verify OTP" onBack={() => navigation.goBack()} />

      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        <Text style={styles.heading}>Enter 6-Digit Code</Text>
        <Text style={styles.subheading}>
          Sent to <Text style={styles.emailHighlight}>{maskedEmail}</Text>
        </Text>

        {__DEV__ && devOtp ? <Notice tone="info" message={`Test mode: your code is ${devOtp}`} /> : null}

        <View style={styles.otpWrap}>
          <OtpCodeInput
            value={otp}
            onChange={v => {
              setOtp(v);
              if (error) setError(null);
            }}
            onComplete={v => handleVerify(v)}
            error={!!error}
          />
        </View>

        {error ? <Notice tone="error" message={error} /> : null}
        {info ? <Notice tone="success" message={info} /> : null}

        <View style={styles.rememberRow}>
          <CheckboxRow
            checked={rememberDevice}
            onToggle={() => setRememberDevice(v => !v)}
            label="Remember this device (skip the code for 30 days)"
          />
        </View>

        <PrimaryButton label="VERIFY & CONTINUE" onPress={() => handleVerify()} loading={verifying} style={styles.verifyBtn} />

        <View style={styles.resendRow}>
          <Text style={styles.resendLabel}>Didn't receive code? </Text>
          {timer.seconds > 0 ? (
            <Text style={styles.timerText}>Resend in {timer.seconds}s</Text>
          ) : (
            <TouchableOpacity onPress={handleResend} disabled={resending || verifying}>
              <Text style={styles.resendBtn}>{resending ? 'Sending…' : 'Resend Code Now'}</Text>
            </TouchableOpacity>
          )}
        </View>
      </ScrollView>
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
    marginBottom: SPACING.md,
  },
  emailHighlight: {
    color: COLORS.text,
    fontWeight: '700',
  },
  otpWrap: {
    width: '100%',
    marginTop: SPACING.lg,
  },
  rememberRow: {
    width: '100%',
    marginTop: SPACING.lg,
  },
  verifyBtn: {
    width: '100%',
    marginTop: SPACING.xl,
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
