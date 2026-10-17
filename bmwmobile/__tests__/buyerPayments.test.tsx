import React from 'react';
import { Alert } from 'react-native';
import ReactTestRenderer from 'react-test-renderer';
import { CheckoutScreen } from '../src/screens/CheckoutScreen';
import { OrderTrackingCard } from '../src/components/OrderTrackingCard';
import { SecondChanceOffers } from '../src/components/SecondChanceOffers';
import { PaymentsApi, checkoutTotalText, centsText, timeLeftText, type CheckoutSession } from '../src/services/paymentsApi';
import { MarketApi } from '../src/services/marketApi';
import { formatMoney } from '../src/utils/geo';
import type { Order } from '../src/types/listing';

jest.mock('@react-navigation/native', () => {
  const { useEffect } = jest.requireActual('react');
  return { useFocusEffect: (fn: () => void) => useEffect(fn, [fn]) };
});
jest.mock('../src/location/LocationContext', () => ({
  useLocations: () => ({ country: 'US', location: null, addresses: [], usingCurrentLocation: true }),
}));
jest.mock('../src/services/paymentsApi', () => {
  const actual = jest.requireActual('../src/services/paymentsApi');
  return {
    ...actual,
    PaymentsApi: {
      buyNow: jest.fn(), payOrder: jest.fn(), cancelPayment: jest.fn(async () => undefined), paymentStatus: jest.fn(),
      offers: jest.fn(), acceptOffer: jest.fn(), declineOffer: jest.fn(),
    },
  };
});
jest.mock('../src/services/marketApi', () => ({ MarketApi: { order: jest.fn(), listingDetail: jest.fn(), chooseDelivery: jest.fn() } }));

const api = PaymentsApi as unknown as Record<string, jest.Mock>;
const sheet = (global as any).__stripeSheet as { initPaymentSheet: jest.Mock; presentPaymentSheet: jest.Mock };
const usd = (n: number, decimals = 0) => formatMoney(n, 'US', decimals);

const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');
const texts = (tree: ReactTestRenderer.ReactTestRenderer) => tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');
const press = async (tree: ReactTestRenderer.ReactTestRenderer, label: string) => {
  // The first match (e.g. the first offer's button).
  let node: ReactTestRenderer.ReactTestInstance | null = tree.root.findAll(n => (n.type as unknown) === 'Text' && textOf(n).startsWith(label))[0];
  while (node && !node.props.onPress) node = node.parent;
  await ReactTestRenderer.act(async () => node!.props.onPress());
};

const mounted: ReactTestRenderer.ReactTestRenderer[] = [];
const render = async (el: React.ReactElement) => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(el);
  });
  mounted.push(tree!);
  return tree!;
};
afterEach(async () => {
  for (const t of mounted.splice(0)) await ReactTestRenderer.act(async () => t.unmount());
});
beforeEach(() => {
  Object.values(api).forEach(m => m.mockReset());
  api.cancelPayment.mockResolvedValue(undefined);
  sheet.initPaymentSheet.mockReset().mockResolvedValue({});
  sheet.presentPaymentSheet.mockReset().mockResolvedValue({});
});

const session = (patch: Partial<CheckoutSession> = {}): CheckoutSession => ({
  paymentId: 'pay1', clientSecret: 'pi_1_secret', customerId: 'cus_1', ephemeralKey: 'ek_1', currency: 'USD',
  itemAmount: 34000, shippingAmount: 2000, totalAmount: 36000, orderId: 'o1', merchantDisplayName: 'ShopCarParts',
  reservedUntil: new Date(Date.now() + 15 * 60000).toISOString(), ...patch,
});

describe('amounts and time left', () => {
  test('checkout total: item + shipping = total, from cents', () => {
    expect(checkoutTotalText(session(), usd)).toBe('Item $340 + Shipping $20 = Total $360');
    expect(checkoutTotalText(session({ shippingAmount: 0, totalAmount: 34000 }), usd)).toBe('Item $340 = Total $340 (pickup, free)');
    expect(centsText(1250, usd)).toBe('$12.50');
    expect(centsText(1250, (n, d) => formatMoney(n, 'IE', d))).toBe('€12.50');
  });

  test('countdown: hours and minutes, then minutes:seconds, then "Time is up"', () => {
    const now = Date.parse('2026-10-15T10:00:00Z');
    expect(timeLeftText('2026-10-16T17:05:00Z', now)).toBe('31 h 5 min');
    expect(timeLeftText('2026-10-15T10:14:32Z', now)).toBe('14:32');
    expect(timeLeftText('2026-10-15T09:59:00Z', now)).toBe('Time is up');
  });
});

describe('Buy now checkout', () => {
  const navigation = { navigate: jest.fn(), goBack: jest.fn() } as any;
  const params = {
    listingId: 'l1', title: 'Silverado alternator', price: 165, quantity: 1, deliveryOptions: ['Pickup', 'Shipping'] as ('Pickup' | 'Shipping')[],
    countryCode: 'US', pickupCity: 'Long Beach',
  };

  test('pickup → the server\'s total and the reservation → Stripe\'s sheet → paid (confirmed by the server)', async () => {
    api.buyNow.mockResolvedValue(session({ itemAmount: 16500, shippingAmount: 0, totalAmount: 16500 }));
    api.paymentStatus.mockResolvedValue({ paymentId: 'pay1', status: 'Succeeded', orderId: 'o1' });
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);

    const tree = await render(<CheckoutScreen navigation={navigation} route={{ key: 'c', name: 'Checkout', params } as any} />);
    await press(tree, '📍 I will pick it up');
    expect(api.buyNow).toHaveBeenCalledWith('l1', 1, { method: 'Pickup' });
    expect(texts(tree)).toContain('Item $165 = Total $165 (pickup, free)');
    expect(texts(tree)).toMatch(/Reserved for you: 1[45]:\d\d/);

    await press(tree, 'PAY $165');
    expect(sheet.initPaymentSheet).toHaveBeenCalledWith(expect.objectContaining({
      paymentIntentClientSecret: 'pi_1_secret', customerId: 'cus_1', customerEphemeralKeySecret: 'ek_1',
      googlePay: expect.objectContaining({ testEnv: true, currencyCode: 'USD' }),
    }));
    expect(sheet.presentPaymentSheet).toHaveBeenCalled();
    expect(api.paymentStatus).toHaveBeenCalledWith('pay1');
    expect(alert).toHaveBeenCalledWith('Paid ✓', expect.any(String), expect.any(Array));
    expect(api.cancelPayment).not.toHaveBeenCalled();
    alert.mockRestore();
  });

  test('declined / closed sheet → the reservation is released at once, back to the choice', async () => {
    api.buyNow.mockResolvedValue(session());
    sheet.presentPaymentSheet.mockResolvedValue({ error: { code: 'Failed', message: 'Your card has insufficient funds.' } });

    const tree = await render(<CheckoutScreen navigation={navigation} route={{ key: 'c', name: 'Checkout', params } as any} />);
    await press(tree, '📍 I will pick it up');
    await press(tree, 'PAY $360');
    expect(api.cancelPayment).toHaveBeenCalledWith('pay1');
    expect(texts(tree)).toContain('Your card has insufficient funds.');
    expect(texts(tree)).toContain('HOW DO YOU WANT TO RECEIVE IT?');
  });

  test('reserved by someone else: the server\'s message', async () => {
    const { ServerError } = jest.requireActual('../src/services/api');
    api.buyNow.mockRejectedValue(new ServerError('Reserved – another buyer is paying for it right now. Try again in a few minutes.', 409));
    const tree = await render(<CheckoutScreen navigation={navigation} route={{ key: 'c', name: 'Checkout', params } as any} />);
    await press(tree, '📍 I will pick it up');
    expect(texts(tree)).toContain('Reserved – another buyer is paying');
  });
});

describe('orders waiting for payment', () => {
  const steps = [{ status: 'SoldAwaitingHandover', label: 'Sold', done: false, current: true, at: null, note: null }];
  const order = (patch: Partial<Order>): Order => ({
    id: 'o1', auctionId: 'a1', role: 'buyer', seller: 'bob', buyer: 'alice', otherParty: 'bob', title: 'Alternator',
    finalTotal: 340, partPrice: 300, winningBid: 40, total: 340, countryCode: 'US', deliveryOptions: ['Pickup'], deliveryMethod: 'Pickup',
    deliveryChoices: [], status: 'AwaitingPayment', statusLabel: 'Awaiting payment', paymentStatus: 'AwaitingPayment',
    paymentDueAt: new Date(Date.now() + (31 * 60 + 5) * 60000 + 30000).toISOString(), canPay: true,
    paymentLabel: 'Waiting for payment – 31 h 5 min left', tracking: steps as Order['tracking'], actions: [], createdAt: '', ...patch,
  });

  test('buyer: countdown and Pay now → paid → the order is reloaded', async () => {
    api.payOrder.mockResolvedValue(session({ itemAmount: 34000, shippingAmount: 0, totalAmount: 34000, reservedUntil: null }));
    api.paymentStatus.mockResolvedValue({ paymentId: 'pay1', status: 'Succeeded', orderId: 'o1' });
    (MarketApi.order as jest.Mock).mockResolvedValue(order({ status: 'SoldAwaitingHandover', paymentStatus: 'Paid', canPay: false }));
    const onChange = jest.fn();
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);

    const tree = await render(<OrderTrackingCard order={order({})} money={usd} onChange={onChange} onOpenListing={jest.fn()} />);
    expect(texts(tree)).toContain('💳 Pay within 31 h 5 min');
    await press(tree, 'PAY NOW – $340');
    expect(api.payOrder).toHaveBeenCalledWith('o1');
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ paymentStatus: 'Paid' }));
    alert.mockRestore();
  });

  test('both options offered and not chosen yet: choose first, no Pay button', async () => {
    const tree = await render(<OrderTrackingCard order={order({ deliveryMethod: null, deliveryOptions: ['Pickup', 'Shipping'], deliveryChoices: ['Pickup', 'Shipping'] })}
      money={usd} onChange={jest.fn()} onOpenListing={jest.fn()} />);
    expect(texts(tree)).toContain('Choose pickup or shipping below first');
    expect(texts(tree)).not.toContain('PAY NOW');
  });

  test('seller: "Waiting for payment – 31 h left"; cancelled: "Not paid in time"', async () => {
    const seller = await render(<OrderTrackingCard order={order({ role: 'seller', canPay: false })} money={usd} onChange={jest.fn()} onOpenListing={jest.fn()} />);
    expect(texts(seller)).toContain('💳 Waiting for payment – 31 h 5 min left');
    expect(texts(seller)).not.toContain('PAY NOW');
    const cancelled = await render(<OrderTrackingCard order={order({ status: 'Cancelled', canPay: false, paymentLabel: 'Not paid in time', paymentStatus: 'Expired' })}
      money={usd} onChange={jest.fn()} onOpenListing={jest.fn()} />);
    expect(texts(cancelled)).toContain('✕ Not paid in time');
  });

  test('India / Nigeria: "Online payment is coming soon in your country"', async () => {
    const tree = await render(<OrderTrackingCard order={order({ countryCode: 'IN', status: 'SoldAwaitingHandover', paymentStatus: 'NotRequired', canPay: false })}
      money={usd} onChange={jest.fn()} onOpenListing={jest.fn()} />);
    expect(texts(tree)).toContain('Online payment is coming soon in your country');
  });
});

test('second-chance offers: accept or decline', async () => {
  api.offers.mockResolvedValue([
    { id: 'f1', listingId: 'a1', title: 'Alternator', amount: 450, currency: 'USD', expiresAt: new Date(Date.now() + 47 * 3600000).toISOString(), round: 1 },
    { id: 'f2', listingId: 'a2', title: 'Radiator', amount: 120, currency: 'USD', expiresAt: new Date(Date.now() + 3600000).toISOString(), round: 2 },
  ]);
  api.acceptOffer.mockResolvedValue({});
  api.declineOffer.mockResolvedValue(undefined);
  const onAccepted = jest.fn();
  const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);

  const tree = await render(<SecondChanceOffers money={usd} onAccepted={onAccepted} />);
  expect(texts(tree)).toContain("It's yours for your highest bid of $450");
  await press(tree, 'ACCEPT & PAY');
  expect(api.acceptOffer).toHaveBeenCalledWith('f1');
  expect(onAccepted).toHaveBeenCalled();
  await press(tree, 'Decline');
  expect(api.declineOffer).toHaveBeenCalledWith('f2');
  expect(texts(tree)).not.toContain('Radiator');
  alert.mockRestore();
});
