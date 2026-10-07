export type UserRole = 'customer' | 'pro';

/** The signed-in user. `username` is the public name on auctions and bids. */
export interface AuthUser {
  id: string;
  username: string;
  fullName: string;
  email: string;
  phoneNumber: string | null;
  /** 'user' while roles are switched off (Identity Features:RolesEnabled = false). */
  role: UserRole | 'user';
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
  /** Only sent when config.ENABLE_ROLES is on. */
  role?: UserRole;
}

export type SocialProvider = 'google' | 'apple';
