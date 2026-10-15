import { getValidAccessToken } from '../auth/session';
import { identityJson } from './authApi';

export type BusinessType = 'Individual' | 'Shop' | 'Dealer' | 'Garage' | 'Scrapyard' | 'Other';
export type Sells = 'Parts' | 'Vehicles' | 'Both';

/** The "Become a seller" form (IdentityService POST /api/seller-applications). */
export interface SellerApplicationRequest {
  fullName: string;
  email: string;
  /** International format, e.g. +919876543210. */
  phoneNumber: string;
  countryCode: string;
  city: string;
  businessType: BusinessType;
  sells: Sells;
  businessName?: string;
  businessAddress?: string;
  taxId?: string;
  message?: string;
}

export type SellerApplicationStatus = 'New' | 'InReview' | 'Approved' | 'Rejected';

/** What the applicant sees about their request (adminNote only when it was rejected). */
export interface MySellerApplication {
  id: string;
  createdAt: string;
  status: SellerApplicationStatus;
  businessName?: string | null;
  adminNote?: string | null;
}

/** "Want to sell?" requests: stored on the server, the admins are emailed, an admin then creates the seller account. */
export const SellerApplicationApi = {
  /** Signed in: linked to the account. Signed out (from Sign up): just the form. */
  submit: async (body: SellerApplicationRequest) =>
    identityJson<MySellerApplication>('post', '/api/seller-applications', body, (await getValidAccessToken()) ?? undefined),

  /** The signed-in user's latest request, or null. */
  mine: async (): Promise<MySellerApplication | null> => {
    const token = await getValidAccessToken();
    if (!token) return null;
    const result = await identityJson<MySellerApplication | ''>('get', '/api/seller-applications/mine', undefined, token);
    return result ? result : null;
  },
};
