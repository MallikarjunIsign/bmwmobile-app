import React, { useEffect, useRef, useState } from 'react';
import { View, Text, StyleSheet, TouchableOpacity, KeyboardAvoidingView, Platform, ScrollView, type TextInputInstance } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING } from '../theme/colors';
import { Header } from '../components/Header';
import { FormInput } from '../components/FormInput';
import { CheckboxRow } from '../components/CheckboxRow';
import { Notice } from '../components/Notice';
import { PrimaryButton } from '../components/PrimaryButton';
import { SocialButtons } from '../components/SocialButtons';
import { useAuth } from '../auth/AuthContext';
import { getRememberedEmail } from '../auth/storage';
import { errorMessage } from '../services/authApi';
import { validateEmail } from '../utils/validation';

type Props = NativeStackScreenProps<RootStackParamList, 'Login'>;

export const LoginScreen: React.FC<Props> = ({ navigation, route }) => {
  const { login, signInWith } = useAuth();
  const passwordRef = useRef<TextInputInstance>(null);

  const [email, setEmail] = useState(route.params?.email ?? '');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(true);
  const [emailError, setEmailError] = useState<string | null>(null);
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [socialBusy, setSocialBusy] = useState(false);

  // "Remember me" fills in the email from last time (never the password).
  useEffect(() => {
    if (route.params?.email) return;
    getRememberedEmail().then(saved => {
      if (saved) setEmail(current => current || saved);
    });
  }, [route.params?.email]);

  const handleLogin = async () => {
    setFormError(null);
    const eErr = validateEmail(email);
    const pErr = password ? null : 'Enter your password';
    setEmailError(eErr);
    setPasswordError(pErr);
    if (eErr || pErr) return;

    setSubmitting(true);
    try {
      const outcome = await login(email, password, rememberMe);
      if (outcome.otpRequired) {
        navigation.navigate('Otp', { ...outcome.challenge, rememberMe });
      }
      // Trusted device: signed in straight away, the navigator switches to the marketplace.
    } catch (err) {
      setFormError(errorMessage(err, 'Login failed. Please try again.'));
    } finally {
      setSubmitting(false);
    }
  };

  const handleSocial = async (provider: 'google' | 'apple') => {
    setFormError(null);
    setSocialBusy(true);
    try {
      await signInWith(provider);
    } catch (err) {
      setFormError(errorMessage(err));
    } finally {
      setSocialBusy(false);
    }
  };

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Welcome Back" subtitle="Sign in to ShopCarParts to buy and sell parts" />

      <KeyboardAvoidingView
        style={styles.keyboardView}
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      >
        <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
          <View style={styles.form}>
            {route.params?.notice ? <Notice tone="success" message={route.params.notice} /> : null}

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
              returnKeyType="next"
              onSubmitEditing={() => passwordRef.current?.focus()}
              error={emailError}
            />

            <FormInput
              ref={passwordRef}
              label="Password"
              value={password}
              onChangeText={t => {
                setPassword(t);
                if (passwordError) setPasswordError(null);
              }}
              placeholder="Enter password"
              secureToggle
              autoCapitalize="none"
              autoComplete="password"
              textContentType="password"
              returnKeyType="done"
              onSubmitEditing={handleLogin}
              error={passwordError}
            />

            <View style={styles.optionsRow}>
              <CheckboxRow checked={rememberMe} onToggle={() => setRememberMe(v => !v)} label="Remember me" />
              <TouchableOpacity onPress={() => navigation.navigate('ForgotPassword', { email: email.trim() })} hitSlop={8}>
                <Text style={styles.forgotText}>Forgot password?</Text>
              </TouchableOpacity>
            </View>

            {formError ? <Notice tone="error" message={formError} /> : null}

            <PrimaryButton
              label="LOG IN"
              onPress={handleLogin}
              loading={submitting}
              disabled={socialBusy}
              style={styles.submitBtn}
            />

            <SocialButtons onGoogle={() => handleSocial('google')} onApple={() => handleSocial('apple')} busy={socialBusy || submitting} />

            <TouchableOpacity style={styles.signUpRow} onPress={() => navigation.navigate('SignUp')} hitSlop={8}>
              <Text style={styles.signUpText}>
                New to ShopCarParts? <Text style={styles.signUpLink}>Create an account</Text>
              </Text>
            </TouchableOpacity>
          </View>
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
  scroll: {
    flexGrow: 1,
    padding: SPACING.md,
  },
  form: {
    marginTop: SPACING.sm,
  },
  optionsRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginTop: SPACING.md,
  },
  forgotText: {
    color: COLORS.primaryGlow,
    fontSize: 13,
    fontWeight: '600',
  },
  submitBtn: {
    marginTop: SPACING.xl,
  },
  signUpRow: {
    alignItems: 'center',
    marginTop: SPACING.lg,
  },
  signUpText: {
    color: COLORS.textMuted,
    fontSize: 14,
  },
  signUpLink: {
    color: COLORS.primaryGlow,
    fontWeight: '700',
  },
});
