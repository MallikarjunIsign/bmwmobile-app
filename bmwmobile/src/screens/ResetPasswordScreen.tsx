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
import { validateEmail, validatePassword } from '../utils/validation';

type Props = NativeStackScreenProps<RootStackParamList, 'ResetPassword'>;

export const ResetPasswordScreen: React.FC<Props> = ({ navigation, route }) => {
  const invite = !!route.params.invite;
  const [email, setEmail] = useState(route.params.email);
  const [emailError, setEmailError] = useState<string | null>(null);
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
    const eErr = validateEmail(email);
    setEmailError(eErr);
    const cErr = code.length === 6 ? null : 'Enter the 6-digit code';
    const pErr = validatePassword(password);
    const mErr = confirm === password ? null : 'Passwords do not match';
    setCodeError(cErr);
    setPasswordError(pErr);
    setConfirmError(mErr);
    if (eErr || cErr || pErr || mErr) return;

    setSubmitting(true);
    try {
      const message = await resetPassword(email, code, password);
      const notice = invite ? 'Password set. Log in with your email and new password.' : message;
      navigation.reset({ index: 1, routes: [{ name: 'Splash' }, { name: 'Login', params: { email: email.trim(), notice } }] });
    } catch (e) {
      setFormError(errorMessage(e));
    } finally {
      setSubmitting(false);
    }
  };

  const handleResend = async () => {
    if (timer.seconds > 0) return;
    const eErr = validateEmail(email);
    setEmailError(eErr);
    if (eErr) return;
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
      <Header
        title={invite ? 'Set Your Password' : 'Set New Password'}
        subtitle={invite ? 'Use the code from your seller invite email' : `Code sent to ${email}`}
        onBack={() => navigation.goBack()}
      />

      <KeyboardAvoidingView style={styles.keyboardView} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          {__DEV__ && devOtp ? <Notice tone="info" message={`Test mode: your code is ${devOtp}`} /> : null}

          {invite ? (
            <FormInput
              label="Email"
              value={email}
              onChangeText={t => {
                setEmail(t);
                if (emailError) setEmailError(null);
              }}
              placeholder="The email your invite was sent to"
              keyboardType="email-address"
              autoCapitalize="none"
              autoComplete="email"
              textContentType="emailAddress"
              error={emailError}
            />
          ) : null}

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

          <PrimaryButton label={invite ? 'SET PASSWORD' : 'UPDATE PASSWORD'} onPress={handleReset} loading={submitting} style={styles.submitBtn} />
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
