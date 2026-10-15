/**
 * Marketplace role, from IdentityService (also the "role" claim in the access token):
 * buyer = everyone who signs up in the app; seller = created or upgraded by an admin; admin = backend only.
 */
export type UserRole = 'buyer' | 'seller' | 'admin';

/** The signed-in user. `username` is the public name on auctions and bids. */
export interface AuthUser {
  id: string;
  username: string;
  fullName: string;
  email: string;
  phoneNumber: string | null;
  /** Decides what the app shows (Sell, My Listings, Sales). The backend enforces the same rules. */
  role: UserRole;
  emailVerified: boolean;
  /** False until the first location is saved ("Set your location" is shown once). */
  hasLocation: boolean;
}

/** Tokens from IdentityService (/connect/token). */
export interface AuthTokens {
  accessToken: string;
  /** Epoch milliseconds. */
  accessTokenExpiresAt: number;
  refreshToken: string | null;
}

export interface AuthSession extends AuthTokens {
  user: AuthUser;
}

/** Email + password was right; a code was emailed (device not trusted yet). */
export interface OtpChallenge {
  challengeId: string;
  maskedEmail: string;
  resendAfterSeconds: number;
  /** Development only: the code, so it can be tested without email. */
  devOtp?: string | null;
}

export interface OtpSentResponse {
  message: string;
  resendAfterSeconds: number;
  devOtp?: string | null;
}

export interface RegisterRequest {
  fullName: string;
  email: string;
  phoneNumber: string;
  password: string;
  acceptTerms: boolean;
}

export type SocialProvider = 'google' | 'apple';
