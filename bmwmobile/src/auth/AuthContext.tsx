import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { Alert } from 'react-native';
import { ApiError, AuthApi, isOtpChallenge, type SignInResult } from '../services/authApi';
import { setDemoOwner } from '../services/api';
import { config } from '../config';
import type { AuthSession, AuthUser, OtpChallenge, OtpSentResponse, RegisterRequest, UserRole } from '../types/auth';
import { getSession, getValidAccessToken, setSession, setTokenHandlers } from './session';
import {
  clearRememberedEmail,
  clearSession,
  getDeviceCredentials,
  loadSession,
  saveDeviceToken,
  saveRememberedEmail,
  saveSession,
} from './storage';
import { SocialCancelled, signInWithApple, signInWithGoogle, signOutSocial, type SocialCredential } from './social';
import { unregisterPush } from '../services/push';

type AuthStatus = 'loading' | 'signedOut' | 'signedIn';

/** Result of email + password: either a code is needed, or the user is already in (trusted device). */
export type LoginOutcome = { otpRequired: true; challenge: OtpChallenge } | { otpRequired: false };

type AuthContextValue = {
  status: AuthStatus;
  user: AuthUser | null;
  register: (req: RegisterRequest) => Promise<void>;
  /** `rememberMe` keeps the user signed in after the app restarts (refresh token in the secure store). */
  login: (email: string, password: string, rememberMe: boolean) => Promise<LoginOutcome>;
  verifyOtp: (challengeId: string, otp: string, rememberDevice: boolean, rememberMe: boolean) => Promise<void>;
  resendOtp: (challengeId: string) => Promise<OtpSentResponse>;
  /** Google / Apple. Resolves false when the user closed the sheet. */
  signInWith: (provider: 'google' | 'apple') => Promise<boolean>;
  forgotPassword: (email: string) => Promise<OtpSentResponse>;
  resetPassword: (email: string, otp: string, newPassword: string) => Promise<string>;
  logout: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

function chooseRole(): Promise<UserRole | null> {
  return new Promise(resolve =>
    Alert.alert('One more step', 'How will you use ShopCarParts?', [
      { text: 'Customer', onPress: () => resolve('customer') },
      { text: 'Service Pro', onPress: () => resolve('pro') },
      { text: 'Cancel', style: 'cancel', onPress: () => resolve(null) },
    ]),
  );
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>('loading');
  const [user, setUser] = useState<AuthUser | null>(null);
  const remember = useRef(false);
  const signingOut = useRef(false);

  const apply = useCallback((session: AuthSession | null) => {
    setSession(session);
    setDemoOwner(session?.user.username ?? null);
    setUser(session?.user ?? null);
  }, []);

  const startSession = useCallback(
    async ({ session, deviceToken }: SignInResult, rememberMe: boolean) => {
      if (deviceToken) await saveDeviceToken(deviceToken);
      remember.current = rememberMe;
      if (rememberMe) await saveSession(session);
      else await clearSession();
      apply(session);
      setStatus('signedIn');
    },
    [apply],
  );

  const endSession = useCallback(async () => {
    // While still signed in: this phone stops getting the user's chat notifications.
    await unregisterPush();
    const refreshToken = getSession()?.refreshToken;
    apply(null);
    setStatus('signedOut');
    await clearSession();
    await signOutSocial();
    if (refreshToken) AuthApi.revoke(refreshToken);
  }, [apply]);

  // The API client asks here for new tokens, and tells us when the session can't be renewed.
  useEffect(() => {
    setTokenHandlers({
      refresh: async refreshToken => {
        try {
          const tokens = await AuthApi.refresh(refreshToken);
          const current = getSession();
          if (!current) return null;
          const next = { ...current, ...tokens };
          setSession(next);
          if (remember.current) await saveSession(next);
          return tokens;
        } catch (err) {
          // Expired / revoked → sign out. Offline → keep the session and let the call fail.
          if (err instanceof ApiError && err.status === 400) return null;
          throw err;
        }
      },
      expired: () => {
        if (signingOut.current) return;
        signingOut.current = true;
        endSession().finally(() => {
          signingOut.current = false;
        });
      },
    });
    return () => setTokenHandlers({ refresh: null, expired: null });
  }, [endSession]);

  // App start: restore a remembered session, then refresh the user from the server.
  useEffect(() => {
    let alive = true;
    (async () => {
      const saved = await loadSession();
      if (!alive) return;
      if (!saved) {
        setStatus('signedOut');
        return;
      }
      remember.current = true;
      apply(saved);
      setStatus('signedIn');
      try {
        const accessToken = await getValidAccessToken();
        if (!accessToken || !alive) return;
        const fresh = await AuthApi.me(accessToken);
        const current = getSession();
        if (alive && current) {
          const next = { ...current, user: fresh };
          apply(next);
          await saveSession(next);
        }
      } catch {
        // Offline is fine: keep the saved user.
      }
    })();
    return () => {
      alive = false;
    };
  }, [apply]);

  const register = useCallback(async (req: RegisterRequest) => {
    await AuthApi.register(req);
  }, []);

  const login = useCallback(
    async (email: string, password: string, rememberMe: boolean): Promise<LoginOutcome> => {
      const device = await getDeviceCredentials();
      const result = await AuthApi.login(email.trim(), password, device.deviceId, device.deviceToken);
      if (rememberMe) await saveRememberedEmail(email.trim());
      else await clearRememberedEmail();
      if (isOtpChallenge(result)) return { otpRequired: true, challenge: result };
      await startSession(result, rememberMe);
      return { otpRequired: false };
    },
    [startSession],
  );

  const verifyOtp = useCallback(
    async (challengeId: string, otp: string, rememberDevice: boolean, rememberMe: boolean) => {
      const device = await getDeviceCredentials();
      const result = await AuthApi.verifyOtp(challengeId, otp.trim(), device.deviceId, rememberDevice);
      await startSession(result, rememberMe);
    },
    [startSession],
  );

  const resendOtp = useCallback((challengeId: string) => AuthApi.resendOtp(challengeId), []);

  const signInWith = useCallback(
    async (provider: 'google' | 'apple') => {
      let credential: SocialCredential;
      try {
        credential = provider === 'google' ? await signInWithGoogle() : await signInWithApple();
      } catch (err) {
        if (err instanceof SocialCancelled) return false;
        throw err;
      }

      const send = (role?: UserRole) =>
        AuthApi.socialLogin({
          provider: credential.provider,
          idToken: credential.idToken,
          nonce: credential.nonce,
          fullName: credential.fullName,
          role,
        });

      let result: SignInResult;
      try {
        result = await send();
      } catch (err) {
        // New account while roles are on: ask Customer / Service Pro, then try again.
        if (!(config.ENABLE_ROLES && err instanceof ApiError && err.code === 'ROLE_REQUIRED')) throw err;
        const role = await chooseRole();
        if (!role) return false;
        result = await send(role);
      }
      await startSession(result, true);
      return true;
    },
    [startSession],
  );

  const forgotPassword = useCallback((email: string) => AuthApi.forgotPassword(email.trim()), []);

  const resetPassword = useCallback(async (email: string, otp: string, newPassword: string) => {
    const res = await AuthApi.resetPassword(email.trim(), otp.trim(), newPassword);
    // The server forgot all trusted devices, so this phone's device token no longer works.
    await saveDeviceToken(null);
    return res.message;
  }, []);

  const logout = useCallback(() => endSession(), [endSession]);

  const value = useMemo<AuthContextValue>(
    () => ({ status, user, register, login, verifyOtp, resendOtp, signInWith, forgotPassword, resetPassword, logout }),
    [status, user, register, login, verifyOtp, resendOtp, signInWith, forgotPassword, resetPassword, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>');
  return ctx;
}
