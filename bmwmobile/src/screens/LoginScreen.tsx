import React, { useState } from 'react';
import { View, Text, StyleSheet, TextInput, TouchableOpacity, KeyboardAvoidingView, Platform } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';

type Props = NativeStackScreenProps<RootStackParamList, 'Login'>;

export const LoginScreen: React.FC<Props> = ({ navigation }) => {
  const [phoneNumber, setPhoneNumber] = useState('+234 803 123 4567');
  const [password, setPassword] = useState('••••••••');

  const handleLogin = () => {
    navigation.navigate('Otp', { phoneNumber });
  };

  return (
    <SafeAreaView style={styles.container}>
      <Header title="Welcome Back" subtitle="Sign in to bid & track container shipments" />

      <KeyboardAvoidingView 
        style={styles.keyboardView} 
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      >
        <View style={styles.form}>
          <Text style={styles.label}>Phone Number or Email</Text>
          <View style={styles.inputWrap}>
            <TextInput
              style={styles.input}
              value={phoneNumber}
              onChangeText={setPhoneNumber}
              placeholder="+234 800 000 0000"
              placeholderTextColor={COLORS.textMuted}
              keyboardType="phone-pad"
            />
          </View>

          <Text style={styles.label}>Password</Text>
          <View style={styles.inputWrap}>
            <TextInput
              style={styles.input}
              value={password}
              onChangeText={setPassword}
              secureTextEntry
              placeholder="Enter password"
              placeholderTextColor={COLORS.textMuted}
            />
          </View>

          <TouchableOpacity style={styles.forgotBtn}>
            <Text style={styles.forgotText}>Forgot password?</Text>
          </TouchableOpacity>

          <TouchableOpacity style={styles.submitBtn} onPress={handleLogin} activeOpacity={0.8}>
            <Text style={styles.submitText}>SEND VERIFICATION OTP</Text>
          </TouchableOpacity>

          <View style={styles.demoNote}>
            <Text style={styles.demoText}>💡 Quick Demo: Tap to log in directly into OKOH Parts</Text>
          </View>
        </View>

        <View style={styles.footer}>
          <TouchableOpacity onPress={() => navigation.navigate('MainTabs')}>
            <Text style={styles.guestText}>Continue as Guest Visitor →</Text>
          </TouchableOpacity>
        </View>
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
    padding: SPACING.md,
    justifyContent: 'space-between',
  },
  form: {
    marginTop: SPACING.lg,
  },
  label: {
    color: COLORS.textSecondary,
    fontSize: 14,
    fontWeight: '600',
    marginBottom: SPACING.xs,
    marginTop: SPACING.md,
  },
  inputWrap: {
    backgroundColor: COLORS.card,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.border,
    paddingHorizontal: SPACING.md,
  },
  input: {
    color: COLORS.text,
    fontSize: 16,
    height: 48,
  },
  forgotBtn: {
    alignSelf: 'flex-end',
    marginTop: SPACING.sm,
  },
  forgotText: {
    color: COLORS.primaryGlow,
    fontSize: 13,
    fontWeight: '600',
  },
  submitBtn: {
    backgroundColor: COLORS.primary,
    paddingVertical: 14,
    borderRadius: RADIUS.md,
    alignItems: 'center',
    marginTop: SPACING.xl,
  },
  submitText: {
    color: COLORS.text,
    fontSize: 15,
    fontWeight: '800',
    letterSpacing: 1,
  },
  demoNote: {
    backgroundColor: COLORS.surface,
    padding: SPACING.md,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.borderHighlight,
    marginTop: SPACING.xl,
  },
  demoText: {
    color: COLORS.textSecondary,
    fontSize: 13,
    textAlign: 'center',
  },
  footer: {
    alignItems: 'center',
    paddingBottom: SPACING.md,
  },
  guestText: {
    color: COLORS.textMuted,
    fontSize: 14,
    fontWeight: '600',
  },
});
