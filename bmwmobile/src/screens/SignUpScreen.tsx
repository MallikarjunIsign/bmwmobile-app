import React, { useRef, useState } from 'react';
import { View, Text, StyleSheet, TouchableOpacity, KeyboardAvoidingView, Platform, ScrollView, Linking, type TextInputInstance } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { FormInput } from '../components/FormInput';
import { PhoneInput } from '../components/PhoneInput';
import { PasswordChecklist } from '../components/PasswordChecklist';
import { CheckboxRow } from '../components/CheckboxRow';
import { Notice } from '../components/Notice';
import { PrimaryButton } from '../components/PrimaryButton';
import { SocialButtons } from '../components/SocialButtons';
import { useAuth } from '../auth/AuthContext';
import { config } from '../config';
import { ApiError, errorMessage } from '../services/authApi';
import { UserRole } from '../types/auth';
import { DEFAULT_COUNTRY, PhoneCountry, toE164, validatePhone } from '../utils/phone';
import { validateEmail, validateFullName, validatePassword } from '../utils/validation';

type Props = NativeStackScreenProps<RootStackParamList, 'SignUp'>;

type Errors = Partial<Record<'fullName' | 'phone' | 'email' | 'password' | 'terms', string | null>>;

export const SignUpScreen: React.FC<Props> = ({ navigation }) => {
  const { register, login, signInWith } = useAuth();
  const emailRef = useRef<TextInputInstance>(null);
  const passwordRef = useRef<TextInputInstance>(null);

  const [role, setRole] = useState<UserRole>('customer');
  const [fullName, setFullName] = useState('');
  const [country, setCountry] = useState<PhoneCountry>(DEFAULT_COUNTRY);
  const [phone, setPhone] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [acceptedTerms, setAcceptedTerms] = useState(false);
  const [errors, setErrors] = useState<Errors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [socialBusy, setSocialBusy] = useState(false);

  const clear = (field: keyof Errors) => setErrors(e => (e[field] ? { ...e, [field]: null } : e));

  const handleSignUp = async () => {
    setFormError(null);
    const next: Errors = {
      fullName: validateFullName(fullName),
      phone: validatePhone(country, phone),
      email: validateEmail(email),
      password: validatePassword(password),
      terms: acceptedTerms ? null : 'Please accept the Terms and Privacy Policy',
    };
    setErrors(next);
    if (Object.values(next).some(Boolean)) return;

    setSubmitting(true);
    try {
      await register({
        fullName: fullName.trim(),
        email: email.trim(),
        phoneNumber: toE164(country, phone),
        password,
        acceptTerms: true,
        ...(config.ENABLE_ROLES ? { role } : {}),
      });
    } catch (err) {
      if (err instanceof ApiError && err.code === 'EMAIL_TAKEN') setErrors(e => ({ ...e, email: err.message }));
      else setFormError(errorMessage(err, 'Sign up failed. Please try again.'));
      setSubmitting(false);
      return;
    }

    // Account created — log straight in, which emails the first code.
    try {
      const outcome = await login(email, password, true);
      if (outcome.otpRequired) navigation.replace('Otp', { ...outcome.challenge, rememberMe: true });
    } catch {
      navigation.replace('Login', { email: email.trim(), notice: 'Your account is ready. Please log in.' });
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
      <Header title="Create Account" subtitle="Bid on BMW parts & track your containers" onBack={() => navigation.goBack()} />

      <KeyboardAvoidingView style={styles.keyboardView} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
          {config.ENABLE_ROLES ? (
            <View style={styles.segment}>
              {(['customer', 'pro'] as UserRole[]).map(r => (
                <TouchableOpacity
                  key={r}
                  onPress={() => setRole(r)}
                  style={[styles.segmentItem, role === r && styles.segmentActive]}
                  activeOpacity={0.8}
                >
                  <Text style={[styles.segmentText, role === r && styles.segmentTextActive]}>
                    {r === 'customer' ? 'Customer' : 'Service Pro'}
                  </Text>
                </TouchableOpacity>
              ))}
            </View>
          ) : null}

          <FormInput
            label="Full Name"
            value={fullName}
            onChangeText={t => {
              setFullName(t);
              clear('fullName');
            }}
            placeholder="Your full name"
            autoCapitalize="words"
            autoComplete="name"
            textContentType="name"
            error={errors.fullName}
          />

          <PhoneInput
            country={country}
            onCountryChange={setCountry}
            value={phone}
            onChangeText={t => {
              setPhone(t);
              clear('phone');
            }}
            error={errors.phone}
          />

          <FormInput
            ref={emailRef}
            label="Email"
            value={email}
            onChangeText={t => {
              setEmail(t);
              clear('email');
            }}
            placeholder="you@example.com"
            keyboardType="email-address"
            autoCapitalize="none"
            autoComplete="email"
            textContentType="emailAddress"
            returnKeyType="next"
            onSubmitEditing={() => passwordRef.current?.focus()}
            error={errors.email}
          />

          <FormInput
            ref={passwordRef}
            label="Password"
            value={password}
            onChangeText={t => {
              setPassword(t);
              clear('password');
            }}
            placeholder="Create a password"
            secureToggle
            autoCapitalize="none"
            autoComplete="new-password"
            textContentType="newPassword"
            error={errors.password && !password ? errors.password : null}
          />
          {password || errors.password ? <PasswordChecklist password={password} /> : null}

          <View style={styles.termsRow}>
            <CheckboxRow
              checked={acceptedTerms}
              onToggle={() => {
                setAcceptedTerms(v => !v);
                clear('terms');
              }}
            >
              <Text style={styles.termsText}>
                I agree to the{' '}
                <Text style={styles.link} onPress={() => Linking.openURL(config.TERMS_URL)}>Terms</Text>
                {' '}and{' '}
                <Text style={styles.link} onPress={() => Linking.openURL(config.PRIVACY_URL)}>Privacy Policy</Text>
              </Text>
            </CheckboxRow>
            {errors.terms ? <Text style={styles.termsError}>{errors.terms}</Text> : null}
          </View>

          {formError ? <Notice tone="error" message={formError} /> : null}

          <PrimaryButton
            label="CREATE ACCOUNT"
            onPress={handleSignUp}
            loading={submitting}
            disabled={socialBusy}
            style={styles.submitBtn}
          />

          <SocialButtons
            onGoogle={() => handleSocial('google')}
            onApple={() => handleSocial('apple')}
            busy={socialBusy || submitting}
            label="or sign up with"
          />

          <TouchableOpacity style={styles.loginRow} onPress={() => navigation.navigate('Login')} hitSlop={8}>
            <Text style={styles.loginText}>
              Already have an account? <Text style={styles.link}>Log in</Text>
            </Text>
          </TouchableOpacity>
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
    padding: SPACING.md,
    paddingBottom: SPACING.xl,
  },
  segment: {
    flexDirection: 'row',
    backgroundColor: COLORS.surface,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.border,
    padding: 4,
    marginTop: SPACING.sm,
  },
  segmentItem: {
    flex: 1,
    paddingVertical: 10,
    borderRadius: RADIUS.sm,
    alignItems: 'center',
  },
  segmentActive: {
    backgroundColor: COLORS.primary,
  },
  segmentText: {
    color: COLORS.textSecondary,
    fontSize: 14,
    fontWeight: '700',
  },
  segmentTextActive: {
    color: COLORS.text,
  },
  termsRow: {
    marginTop: SPACING.lg,
  },
  termsText: {
    color: COLORS.textSecondary,
    fontSize: 13,
    flexShrink: 1,
  },
  termsError: {
    color: COLORS.danger,
    fontSize: 12,
    fontWeight: '600',
    marginTop: SPACING.xs,
  },
  link: {
    color: COLORS.primaryGlow,
    fontWeight: '700',
  },
  submitBtn: {
    marginTop: SPACING.xl,
  },
  loginRow: {
    alignItems: 'center',
    marginTop: SPACING.lg,
  },
  loginText: {
    color: COLORS.textMuted,
    fontSize: 14,
  },
});
