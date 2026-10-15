import { Auction, Purchase, PartRequest } from './models';

export type RootStackParamList = {
  Splash: undefined;
  Login: { email?: string; notice?: string } | undefined;
  SignUp: undefined;
  Otp: { challengeId: string; maskedEmail: string; resendAfterSeconds: number; devOtp?: string | null; rememberMe: boolean };
  ForgotPassword: { email?: string } | undefined;
  /** invite: a new seller sets their first password with the code from the invite email (email can be typed). */
  ResetPassword: { email: string; resendAfterSeconds: number; devOtp?: string | null; invite?: boolean };
  /** The signed-in user: role, saved locations and addresses, My Garage; sellers also My Listings and their reviews. */
  Profile: undefined;
  /** "Want to sell?": the seller application form (signed in from Profile, or signed out from Sign up with the email typed). */
  SellerApplication: { email?: string } | undefined;
  /** Sellers: open part requests from buyers ("A buyer is looking for …"). */
  BuyerRequests: undefined;
  ProfilePreferences: undefined;

  MainTabs: { screen?: string } | undefined;

  /** showOutcome: opened from a "You won" / "Auction lost" notification → go on to that screen once loaded. */
  PartDetails: { auctionId: string; auction?: Auction; showOutcome?: boolean };
  PlaceBidModal: { auction: Auction };
  HighestBidder: { auction: Auction };
  Outbid: { auction: Auction };
  EndingSoon: { auction: Auction };
  AuctionWon: { auction: Auction };
  AuctionLost: { auction: Auction };

  CompletePurchase: { auctionId: string; auction?: Auction };
  PaymentConfirmed: { purchase: Purchase };
  PickupReceive: { purchase: Purchase };

  PartUnavailable: { auctionTitle?: string; make?: string; model?: string };
  /** Custom order, pre-filled from the search text / chips that found nothing. */
  OrderPartRequest: {
    prefillMake?: string;
    prefillModel?: string;
    prefillPart?: string;
    prefillYear?: number;
    prefillPartNumber?: string;
    searchText?: string;
  } | undefined;
  RequestSubmitted: { request: PartRequest };
  PlannedContainers: undefined;

  /** "+ Sell": edit a draft (or a Live listing without bids). step = open a step directly ("photos"). */
  SellWizard: { listingId: string; step?: string };
  MyListings: undefined;
  /** A seller's profile: ratings, reviews and everything they have listed (tapping the seller on a card / listing). */
  SellerProfile: { seller: string; sellerName?: string };
  /** "How was your purchase?": write (orderId) or view / edit (reviewId) the review of an order. */
  WriteReview: { orderId: string; reviewId?: string | null; title: string; imageUrl?: string | null; seller: string };
  /** Messages (chat inbox), opened from the Home header. */
  Inbox: undefined;
  /**
   * One chat: by id (inbox, notification), or about a listing – "Chat with seller" (listingId) or the seller's
   * "Message buyer" on an order (listingId + buyer).
   */
  Conversation: { conversationId?: string; listingId?: string; buyer?: string };
  Garage: undefined;
  /** My locations: current location (GPS) or a saved address. */
  Locations: undefined;
  /** Add (no id) or edit a saved address. */
  AddressForm: { id?: string };
};

export type MainTabParamList = {
  Home: undefined;
  Marketplace: { searchTerm?: string; category?: string } | undefined;
  Sell: undefined;
  MyBids: undefined;
  MyPurchases: undefined;
  Tracking: { containerNumber?: string } | undefined;
};
