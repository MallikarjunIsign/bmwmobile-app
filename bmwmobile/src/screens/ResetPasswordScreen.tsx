import React, { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, KeyboardAvoidingView, Platform, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING } from '../theme/colors';
import { Header } from '../components/Header';
import { FormInput } from '../components/FormInput';
import { OtpCodeInput } from '../components/OtpCodeInput';
import { PasswordChecklist } from '../components/PasswordChecklist';
import { Notice } from '../components/Notice';
import { PrimaryButton } from '../components/PrimaryButton';
import { useAuth } from '../auth/AuthContext';
import { useCountdown } from '../hooks/useCountdown';
import { errorMessage } from '../services/authApi';
import { validatePassword } from '../utils/validation';

type Props = NativeStackScreenProps<RootStackParamList, 'ResetPassword'>;

export const ResetPasswordScreen: React.FC<Props> = ({ navigation, route }) => {
  const { email } = route.params;
  const { resetPassword, forgotPassword } = useAuth();
  const timer = useCountdown(route.params.resendAfterSeconds);

  const [code, setCode] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [devOtp, setDevOtp] = useState(route.params.devOtp ?? null);
  const [codeError, setCodeError] = useState<string | null>(null);
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [confirmError, setConfirmError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const handleReset = async () => {
    setFormError(null);
    setInfo(null);
    const cErr = code.length === 6 ? null : 'Enter the 6-digit code';
    const pErr = validatePassword(password);
    const mErr = confirm === password ? null : 'Passwords do not match';
    setCodeError(cErr);
    setPasswordError(pErr);
    setConfirmError(mErr);
    if (cErr || pErr || mErr) return;

    setSubmitting(true);
    try {
      const message = await resetPassword(email, code, password);
      navigation.reset({ index: 1, routes: [{ name: 'Splash' }, { name: 'Login', params: { email, notice: message } }] });
    } catch (e) {
      setFormError(errorMessage(e));
    } finally {
      setSubmitting(false);
    }
  };

  const handleResend = async () => {
    if (timer.seconds > 0) return;
    setFormError(null);
    try {
      const res = await forgotPassword(email);
      setDevOtp(res.devOtp ?? null);
      setCode('');
      setInfo('New code sent. Check your email.');
      timer.restart(res.resendAfterSeconds);
    } catch (e) {
      setFormError(errorMessage(e));
    }
  };

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Set New Password" subtitle={`Code sent to ${email}`} onBack={() => navigation.goBack()} />

      <KeyboardAvoidingView style={styles.keyboardView} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          {__DEV__ && devOtp ? <Notice tone="info" message={`Test mode: your code is ${devOtp}`} /> : null}

          <Text style={styles.label}>Verification Code</Text>
          <OtpCodeInput
            value={code}
            onChange={v => {
              setCode(v);
              if (codeError) setCodeError(null);
            }}
            error={!!codeError}
            autoFocus={false}
          />
          {codeError ? <Text style={styles.error}>{codeError}</Text> : null}

          <View style={styles.resendRow}>
            <Text style={styles.resendLabel}>Didn't receive code? </Text>
            {timer.seconds > 0 ? (
              <Text style={styles.timerText}>Resend in {timer.seconds}s</Text>
            ) : (
              <TouchableOpacity onPress={handleResend}>
                <Text style={styles.resendBtn}>Resend Code Now</Text>
              </TouchableOpacity>
            )}
          </View>

          <FormInput
            label="New Password"
            value={password}
            onChangeText={t => {
              setPassword(t);
              if (passwordError) setPasswordError(null);
            }}
            placeholder="Create a new password"
            secureToggle
            autoCapitalize="none"
            autoComplete="new-password"
            textContentType="newPassword"
            error={passwordError && !password ? passwordError : null}
          />
          {password || passwordError ? <PasswordChecklist password={password} /> : null}

          <FormInput
            label="Confirm New Password"
            value={confirm}
            onChangeText={t => {
              setConfirm(t);
              if (confirmError) setConfirmError(null);
            }}
            placeholder="Type it again"
            secureToggle
            autoCapitalize="none"
            textContentType="newPassword"
            error={confirmError}
          />

          {formError ? <Notice tone="error" message={formError} /> : null}
          {info ? <Notice tone="success" message={info} /> : null}

          <PrimaryButton label="UPDATE PASSWORD" onPress={handleReset} loading={submitting} style={styles.submitBtn} />
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.background,
  },
  keyboardView: {
    flex: 1,
  },
  content: {
    padding: SPACING.md,
    paddingBottom: SPACING.xl,
  },
  label: {
    color: COLORS.textSecondary,
    fontSize: 14,
    fontWeight: '600',
    marginBottom: SPACING.sm,
    marginTop: SPACING.md,
  },
  error: {
    color: COLORS.danger,
    fontSize: 12,
    fontWeight: '600',
    marginTop: SPACING.xs,
  },
  resendRow: {
    flexDirection: 'row',
    justifyContent: 'center',
    marginTop: SPACING.md,
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
  submitBtn: {
    marginTop: SPACING.xl,
  },
});
