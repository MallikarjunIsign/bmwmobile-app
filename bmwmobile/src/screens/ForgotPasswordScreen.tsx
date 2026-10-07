import React, { useState } from 'react';
import { Text, StyleSheet, ScrollView, KeyboardAvoidingView, Platform } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING } from '../theme/colors';
import { Header } from '../components/Header';
import { FormInput } from '../components/FormInput';
import { Notice } from '../components/Notice';
import { PrimaryButton } from '../components/PrimaryButton';
import { useAuth } from '../auth/AuthContext';
import { errorMessage } from '../services/authApi';
import { validateEmail } from '../utils/validation';

type Props = NativeStackScreenProps<RootStackParamList, 'ForgotPassword'>;

export const ForgotPasswordScreen: React.FC<Props> = ({ navigation, route }) => {
  const { forgotPassword } = useAuth();
  const [email, setEmail] = useState(route.params?.email ?? '');
  const [emailError, setEmailError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const handleSend = async () => {
    setFormError(null);
    const err = validateEmail(email);
    setEmailError(err);
    if (err) return;

    setSubmitting(true);
    try {
      const res = await forgotPassword(email);
      navigation.navigate('ResetPassword', {
        email: email.trim(),
        resendAfterSeconds: res.resendAfterSeconds,
        devOtp: res.devOtp,
      });
    } catch (e) {
      setFormError(errorMessage(e));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Forgot Password" subtitle="We'll email you a code to set a new one" onBack={() => navigation.goBack()} />

      <KeyboardAvoidingView style={styles.keyboardView} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <Text style={styles.heading}>🔑 Reset your password</Text>
          <Text style={styles.subheading}>Enter the email you signed up with.</Text>

          <FormInput
            label="Email"
            value={email}
            onChangeText={t => {
              setEmail(t);
              if (emailError) setEmailError(null);
            }}
            placeholder="you@example.com"
            keyboardType="email-address"
            autoCapitalize="none"
            autoComplete="email"
            textContentType="emailAddress"
            returnKeyType="send"
            onSubmitEditing={handleSend}
            error={emailError}
          />

          {formError ? <Notice tone="error" message={formError} /> : null}

          <PrimaryButton label="SEND RESET CODE" onPress={handleSend} loading={submitting} style={styles.submitBtn} />
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
  },
  submitBtn: {
    marginTop: SPACING.xl,
  },
});
