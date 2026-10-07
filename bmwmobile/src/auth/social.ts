import { Platform } from 'react-native';
import {
  GoogleSignin,
  isErrorWithCode,
  isSuccessResponse,
  statusCodes,
} from '@react-native-google-signin/google-signin';
import appleAuth from '@invertase/react-native-apple-authentication';
import { config } from '../config';
import type { SocialProvider } from '../types/auth';

export type SocialCredential = {
  provider: SocialProvider;
  idToken: string;
  /** Apple only: the raw nonce; Apple puts its SHA-256 in the token and the server checks it. */
  nonce?: string | null;
  /** Apple only sends the name on the very first sign-in, so we pass it on when we get it. */
  fullName?: string | null;
};

/** Thrown when the user closes the Google / Apple sheet — not an error to show. */
export class SocialCancelled extends Error {
  constructor() {
    super('cancelled');
    this.name = 'SocialCancelled';
  }
}

let googleConfigured = false;

/** Apple sign-in is iOS only (13+). */
export const isAppleAvailable = () => Platform.OS === 'ios' && appleAuth.isSupported;

export async function signInWithGoogle(): Promise<SocialCredential> {
  if (!config.GOOGLE_WEB_CLIENT_ID) {
    throw new Error("Google sign-in isn't set up yet. Add GOOGLE_WEB_CLIENT_ID in src/config.ts.");
  }
  if (!googleConfigured) {
    GoogleSignin.configure({
      webClientId: config.GOOGLE_WEB_CLIENT_ID,
      iosClientId: config.GOOGLE_IOS_CLIENT_ID || undefined,
    });
    googleConfigured = true;
  }

  try {
    await GoogleSignin.hasPlayServices({ showPlayServicesUpdateDialog: true });
    const response = await GoogleSignin.signIn();
    if (!isSuccessResponse(response)) throw new SocialCancelled();
    const idToken = response.data.idToken;
    if (!idToken) throw new Error('Google did not return a sign-in token. Please try again.');
    return { provider: 'google', idToken, fullName: response.data.user.name };
  } catch (err) {
    if (err instanceof SocialCancelled) throw err;
    if (isErrorWithCode(err)) {
      if (err.code === statusCodes.SIGN_IN_CANCELLED) throw new SocialCancelled();
      if (err.code === statusCodes.IN_PROGRESS) throw new Error('Google sign-in is already in progress.');
      if (err.code === statusCodes.PLAY_SERVICES_NOT_AVAILABLE) {
        throw new Error('Google Play services is not available on this device.');
      }
    }
    throw err instanceof Error ? err : new Error('Google sign-in failed. Please try again.');
  }
}

export async function signInWithApple(): Promise<SocialCredential> {
  try {
    const res = await appleAuth.performRequest({
      requestedOperation: appleAuth.Operation.LOGIN,
      requestedScopes: [appleAuth.Scope.FULL_NAME, appleAuth.Scope.EMAIL],
    });
    if (!res.identityToken) throw new Error('Apple did not return a sign-in token. Please try again.');
    const fullName = [res.fullName?.givenName, res.fullName?.familyName].filter(Boolean).join(' ').trim();
    return { provider: 'apple', idToken: res.identityToken, nonce: res.nonce, fullName: fullName || null };
  } catch (err) {
    if ((err as { code?: string })?.code === appleAuth.Error.CANCELED) throw new SocialCancelled();
    if (err instanceof Error && err.message.startsWith('Apple did not')) throw err;
    throw new Error('Could not sign in with Apple. Make sure this iPhone is signed in to iCloud and try again.');
  }
}

export async function signOutSocial(): Promise<void> {
  try {
    if (googleConfigured) await GoogleSignin.signOut();
  } catch {
    /* best effort */
  }
}
