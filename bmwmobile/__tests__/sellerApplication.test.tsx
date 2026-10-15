import React from 'react';
import ReactTestRenderer from 'react-test-renderer';
import { SellerApplicationScreen } from '../src/screens/SellerApplicationScreen';
import { ProfileScreen } from '../src/screens/ProfileScreen';
import { SellerApplicationApi } from '../src/services/sellerApplicationApi';
import { ApiError } from '../src/services/authApi';

jest.setTimeout(20000);

let mockUser: object | null = null;
jest.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ status: mockUser ? 'signedIn' : 'signedOut', user: mockUser, logout: jest.fn() }) }));
let mockLocation: object | null = null;
jest.mock('../src/location/LocationContext', () => ({ useLocations: () => ({ location: mockLocation, country: 'IN' }) }));
jest.mock('../src/services/sellerApplicationApi', () => ({ SellerApplicationApi: { submit: jest.fn(), mine: jest.fn() } }));
// Profile loads on focus; outside a navigator, on mount.
jest.mock('@react-navigation/native', () => {
  const actual = jest.requireActual('@react-navigation/native');
  const { useEffect } = jest.requireActual('react');
  return { ...actual, useFocusEffect: (effect: () => void) => useEffect(effect, [effect]) };
});

const api = SellerApplicationApi as unknown as { submit: jest.Mock; mine: jest.Mock };
const navigation = { navigate: jest.fn(), goBack: jest.fn() } as any;

const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');
const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');
const press = async (tree: ReactTestRenderer.ReactTestRenderer, label: string) => {
  let node: ReactTestRenderer.ReactTestInstance | null = tree.root.find(n => (n.type as unknown) === 'Text' && textOf(n) === label);
  while (node && !node.props.onPress) node = node.parent;
  await ReactTestRenderer.act(async () => node!.props.onPress());
};
const type = async (tree: ReactTestRenderer.ReactTestRenderer, label: string, value: string) => {
  const input = tree.root.find(n => n.props.accessibilityLabel === label && n.props.onChangeText);
  await ReactTestRenderer.act(async () => input.props.onChangeText(value));
};

const mounted: ReactTestRenderer.ReactTestRenderer[] = [];
const mount = async (element: React.ReactElement) => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(element);
  });
  mounted.push(tree!);
  return tree!;
};
afterEach(async () => {
  for (const tree of mounted.splice(0)) await ReactTestRenderer.act(async () => tree.unmount());
  mockUser = null;
  mockLocation = null;
});
beforeEach(() => {
  api.submit.mockReset().mockImplementation(async () => ({ id: 'a1', createdAt: '2026-10-14T10:00:00Z', status: 'New' }));
  api.mine.mockReset().mockResolvedValue(null);
  navigation.navigate.mockReset();
});

const form = (email?: string) => (
  <SellerApplicationScreen navigation={navigation} route={{ key: 'f', name: 'SellerApplication', params: email ? { email } : undefined } as any} />
);

describe('"Become a seller" form', () => {
  test('signed out (from Sign up): the typed email is filled in; nothing is sent until the required fields are right', async () => {
    const tree = await mount(form('ravi@example.com'));
    expect(tree.root.find(n => n.props.accessibilityLabel === 'Email' && n.props.onChangeText).props.value).toBe('ravi@example.com');

    await press(tree, 'SEND TO OUR TEAM');
    const t = texts(tree);
    expect(t).toContain('Please fix the fields marked in red.');
    expect(t).toContain('Enter your city');
    expect(t).toContain('Choose what kind of seller you are');
    expect(t).toContain('Choose what you want to sell');
    expect(api.submit).not.toHaveBeenCalled();
  });

  test('contact + business details are sent in one request, then "Request sent"', async () => {
    const tree = await mount(form());
    await type(tree, 'Full name', 'Ravi Kumar');
    await type(tree, 'Email', 'ravi@example.com');
    await type(tree, 'Mobile number', '9876543210');
    await type(tree, 'City (India)', 'Hyderabad');
    await press(tree, 'Parts shop');
    await press(tree, 'Both');
    await type(tree, 'Business name', 'Ravi Auto Parts');
    await type(tree, 'Business address (optional)', '12 MG Road, Ameerpet');
    await type(tree, 'GSTIN (optional)', '36ABCDE1234F1Z5');
    await type(tree, 'Anything else? (optional)', 'Used bumpers');
    await press(tree, 'SEND TO OUR TEAM');

    expect(api.submit).toHaveBeenCalledWith({
      fullName: 'Ravi Kumar', email: 'ravi@example.com', phoneNumber: '+919876543210', countryCode: 'IN', city: 'Hyderabad',
      businessType: 'Shop', sells: 'Both', businessName: 'Ravi Auto Parts', businessAddress: '12 MG Road, Ameerpet',
      taxId: '36ABCDE1234F1Z5', message: 'Used bumpers',
    });
    const t = texts(tree);
    expect(t).toContain('Thanks, Ravi!');
    expect(t).toContain('ravi@example.com');
  });

  test('a shop needs a business name; an individual seller does not', async () => {
    const tree = await mount(form('ravi@example.com'));
    await type(tree, 'Full name', 'Ravi Kumar');
    await type(tree, 'Mobile number', '9876543210');
    await type(tree, 'City (India)', 'Hyderabad');
    await press(tree, 'Car dealer');
    await press(tree, 'Vehicles');
    await press(tree, 'SEND TO OUR TEAM');
    expect(texts(tree)).toContain('Enter your business name');
    expect(api.submit).not.toHaveBeenCalled();

    await press(tree, 'Individual');
    await press(tree, 'SEND TO OUR TEAM');
    expect(api.submit).toHaveBeenCalledWith(expect.objectContaining({ businessType: 'Individual', sells: 'Vehicles', businessName: undefined }));
  });

  test('signed in: name, email, phone, city and country come from the account and location', async () => {
    mockUser = { id: 'u1', fullName: 'Sita Devi', email: 'sita@example.com', phoneNumber: '+2348031234567', role: 'buyer' };
    mockLocation = { city: 'Lagos', countryCode: 'NG' };
    const tree = await mount(form());
    const value = (label: string) => tree.root.find(n => n.props.accessibilityLabel === label && n.props.onChangeText).props.value;
    expect(value('Full name')).toBe('Sita Devi');
    expect(value('Email')).toBe('sita@example.com');
    expect(value('Mobile number')).toBe('8031234567');
    expect(value('City (Nigeria)')).toBe('Lagos');
    expect(texts(tree)).toContain('TIN (optional)');
  });

  test('a request already waiting: the server\'s message is shown', async () => {
    api.submit.mockRejectedValue(new ApiError('We already have your request from 14 Oct 2026. Our team will contact you soon.', 409, 'APPLICATION_PENDING'));
    mockUser = { id: 'u1', fullName: 'Sita Devi', email: 'sita@example.com', phoneNumber: '+919876543210', role: 'buyer' };
    mockLocation = { city: 'Hyderabad', countryCode: 'IN' };
    const tree = await mount(form());
    await press(tree, 'Individual');
    await press(tree, 'Parts');
    await press(tree, 'SEND TO OUR TEAM');
    expect(texts(tree)).toContain('We already have your request from 14 Oct 2026. Our team will contact you soon.');
    expect(texts(tree)).not.toContain('Thanks,');
  });
});

describe('Profile: where the buyer\'s request is', () => {
  const buyer = { id: 'u1', username: 'sita', fullName: 'Sita Devi', email: 'sita@example.com', phoneNumber: null, role: 'buyer' };
  const profile = () => <ProfileScreen navigation={navigation} route={{ key: 'p', name: 'Profile' } as any} />;

  test('no request yet → APPLY TO SELL opens the form', async () => {
    mockUser = buyer;
    const tree = await mount(profile());
    await press(tree, 'APPLY TO SELL');
    expect(navigation.navigate).toHaveBeenCalledWith('SellerApplication');
  });

  test('request waiting → "Request sent on …", no second request', async () => {
    mockUser = buyer;
    api.mine.mockResolvedValue({ id: 'a1', createdAt: '2026-10-14T10:00:00Z', status: 'InReview' });
    const tree = await mount(profile());
    const t = texts(tree);
    expect(t).toContain('Request sent on 14 Oct 2026');
    expect(t).not.toContain('APPLY TO SELL');
  });

  test('rejected → the admin\'s note, and they can apply again', async () => {
    mockUser = buyer;
    api.mine.mockResolvedValue({ id: 'a1', createdAt: '2026-10-14T10:00:00Z', status: 'Rejected', adminNote: 'Please add your GST number.' });
    const tree = await mount(profile());
    const t = texts(tree);
    expect(t).toContain("wasn't approved: Please add your GST number.");
    expect(t).toContain('APPLY TO SELL');
  });
});
