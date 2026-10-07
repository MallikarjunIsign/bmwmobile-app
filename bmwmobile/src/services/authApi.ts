import axios, { AxiosError } from 'axios';
import { config } from '../config';
import type {
  AuthSession,
  AuthTokens,
  AuthUser,
  OtpChallenge,
  OtpSentResponse,
  RegisterRequest,
} from '../types/auth';

/** Error with a message that is safe to show, and the server's code (e.g. OTP_REQUIRED, EMAIL_TAKEN). */
export class ApiError extends Error {
  status: number;
  code?: string;
  data?: any;

  constructor(message: string, status: number, code?: string, data?: any) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.data = data;
  }
}

export const errorMessage = (err: unknown, fallback = 'Something went wrong. Please try again.') =>
  err instanceof Error && err.message ? err.message : fallback;

const identity = axios.create({
  baseURL: config.IDENTITY_URL,
  timeout: config.AUTH_TIMEOUT_MS,
  headers: { Accept: 'application/json' },
});

/** Turns network / server errors into ApiError with a friendly message. */
function toApiError(err: unknown): ApiError {
  const e = err as AxiosError<any>;
  if (!e.response) {
    return new ApiError(
      e.code === 'ECONNABORTED'
        ? 'The server is taking too long to respond. Please try again.'
        : "Can't reach the server. Check your internet connection and try again.",
      0,
      e.code === 'ECONNABORTED' ? 'TIMEOUT' : 'NETWORK',
    );
  }
  const data = e.response.data ?? {};
  const message =
    data.message ??
    data.error_description ??
    (e.response.status >= 500 ? 'Something went wrong on our side. Please try again.' : 'Request failed. Please try again.');
  return new ApiError(message, e.response.status, data.code ?? data.error, data);
}

const form = (fields: Record<string, string | undefined | null>) =>
  Object.entries(fields)
    .filter(([, v]) => v !== undefined && v !== null && v !== '')
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
    .join('&');

type TokenResponse = {
  access_token: string;
  expires_in: number;
  refresh_token?: string;
  user?: AuthUser;
  deviceToken?: string;
};

async function token(fields: Record<string, string | undefined | null>): Promise<TokenResponse> {
  try {
    const res = await identity.post<TokenResponse>(
      '/connect/token',
      form({ client_id: config.OAUTH_CLIENT_ID, ...fields }),
      { headers: { 'Content-Type': 'application/x-www-form-urlencoded' } },
    );
    return res.data;
  } catch (err) {
    throw toApiError(err);
  }
}

const toTokens = (res: TokenResponse): AuthTokens => ({
  accessToken: res.access_token,
  accessTokenExpiresAt: Date.now() + res.expires_in * 1000,
  refreshToken: res.refresh_token ?? null,
});

/** JSON call to IdentityService (account and location APIs). */
export async function identityJson<T>(method: 'get' | 'post' | 'put' | 'delete', path: string, body?: unknown, accessToken?: string): Promise<T> {
  try {
    const res = await identity.request<T>({
      method,
      url: path,
      data: body,
      headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
    });
    return res.data;
  } catch (err) {
    throw toApiError(err);
  }
}

/** Signed in: tokens + user (+ device token when "remember this device" was ticked). */
export type SignInResult = { session: AuthSession; deviceToken?: string };

const signedIn = (res: TokenResponse): SignInResult => ({
  session: { ...toTokens(res), user: res.user as AuthUser },
  deviceToken: res.deviceToken,
});

export const AuthApi = {
  register: (req: RegisterRequest) => identityJson<AuthUser>('post', '/api/account/register', req),

  /** Email + password. A trusted device is signed in straight away; otherwise a code is emailed. */
  login: async (
    email: string,
    password: string,
    deviceId: string,
    deviceToken: string | null,
  ): Promise<SignInResult | OtpChallenge> => {
    try {
      return signedIn(
        await token({
          grant_type: 'bmw_password',
          scope: config.OAUTH_SCOPE,
          username: email,
          password,
          device_id: deviceId,
          device_token: deviceToken,
        }),
      );
    } catch (err) {
      if (err instanceof ApiError && err.code === 'OTP_REQUIRED') {
        return {
          challengeId: err.data.challengeId,
          maskedEmail: err.data.maskedEmail ?? email,
          resendAfterSeconds: err.data.resendAfterSeconds ?? 30,
          devOtp: err.data.devOtp,
        };
      }
      throw err;
    }
  },

  verifyOtp: async (challengeId: string, otp: string, deviceId: string, rememberDevice: boolean) =>
    signedIn(
      await token({
        grant_type: 'bmw_otp',
        scope: config.OAUTH_SCOPE,
        challenge_id: challengeId,
        otp,
        device_id: deviceId,
        remember_device: rememberDevice ? 'true' : 'false',
      }),
    ),

  resendOtp: (challengeId: string) => identityJson<OtpSentResponse>('post', '/api/account/otp/resend', { challengeId }),

  socialLogin: async (fields: {
    provider: 'google' | 'apple';
    idToken: string;
    nonce?: string | null;
    fullName?: string | null;
    role?: string | null;
  }) =>
    signedIn(
      await token({
        grant_type: 'bmw_external',
        scope: config.OAUTH_SCOPE,
        provider: fields.provider,
        id_token: fields.idToken,
        nonce: fields.nonce,
        full_name: fields.fullName,
        role: fields.role,
      }),
    ),

  /** New access token from a refresh token ("Remember me"). Refresh tokens are one-time use. */
  refresh: async (refreshToken: string) => toTokens(await token({ grant_type: 'refresh_token', refresh_token: refreshToken })),

  /** Log out: the refresh token stops working on the server. */
  revoke: async (refreshToken: string) => {
    try {
      await identity.post(
        '/connect/revocation',
        form({ client_id: config.OAUTH_CLIENT_ID, token: refreshToken, token_type_hint: 'refresh_token' }),
        { headers: { 'Content-Type': 'application/x-www-form-urlencoded' } },
      );
    } catch {
      // Best effort: the local session is cleared anyway.
    }
  },

  forgotPassword: (email: string) => identityJson<OtpSentResponse>('post', '/api/account/forgot-password', { email }),

  resetPassword: (email: string, otp: string, newPassword: string) =>
    identityJson<{ message: string }>('post', '/api/account/reset-password', { email, otp, newPassword }),

  me: (accessToken: string) => identityJson<AuthUser>('get', '/api/account/me', undefined, accessToken),
};

export const isOtpChallenge = (r: SignInResult | OtpChallenge): r is OtpChallenge => 'challengeId' in r;
