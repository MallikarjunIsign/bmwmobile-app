import React, { useCallback, useEffect, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Image, TouchableOpacity, Alert, Modal, TextInput, Dimensions, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { NativeStackScreenProps } from '@react-navigation/native-stack';
import { RootStackParamList } from '../types/navigation';
import { COLORS, SPACING, RADIUS } from '../theme/colors';
import { Header } from '../components/Header';
import { Badge } from '../components/Badge';
import { listingErrorMessage, mapRawDtoToAuction, ServerError } from '../services/api';
import { MarketApi } from '../services/marketApi';
import { isCurrentUser } from '../auth/session';
import { useAuth } from '../auth/AuthContext';
import { isSeller as isSellerAccount } from '../utils/roles';
import { useMoney } from '../location/useMoney';
import { useLocations } from '../location/LocationContext';
import { FEATURES } from '../config';
import { useNextBid } from '../hooks/useNextBid';
import { useLiveEvent } from '../services/notifications';
import { NextBidCalculation } from '../components/NextBidCalculation';
import { Countdown } from '../components/Countdown';
import { formatDistance, formatRadius, haversineKm } from '../utils/geo';
import { label, maskUsername } from '../utils/time';
import { buyerDeliveryText, deliverySummary, receiveProblem, SHIPPING_NOT_AVAILABLE, shippingEstimateText, shipsWithinText } from '../utils/delivery';
import { useShippingEstimate } from '../hooks/useShippingEstimate';
import { usePaymentConfig } from '../payments/PaymentsProvider';
import { ratingText } from '../utils/rating';
import { ReviewApi } from '../services/reviewApi';
import { ReviewCard } from '../components/ReviewCard';
import type { Review } from '../types/review';
import type { Bid } from '../types/models';
import type { Listing } from '../types/listing';

type Props = NativeStackScreenProps<RootStackParamList, 'PartDetails'>;

const WIDTH = Dimensions.get('window').width;

const Row: React.FC<{ k: string; v?: string | number | null }> = ({ k, v }) =>
  v === undefined || v === null || v === '' ? null : (
    <View style={styles.infoRow}>
      <Text style={styles.infoKey}>{k}</Text>
      <Text style={styles.infoValue}>{v}</Text>
    </View>
  );

/** For someone who bid on a sold listing: which result screen to show (null for the seller, non-bidders, unsold). */
export function bidOutcome(listing: Pick<Listing, 'status' | 'seller' | 'winner'>, bids: Pick<Bid, 'bidder'>[]): 'AuctionWon' | 'AuctionLost' | null {
  if (!['Sold', 'Completed'].includes(listing.status) || isCurrentUser(listing.seller)) return null;
  if (listing.winner && isCurrentUser(listing.winner)) return 'AuctionWon';
  return bids.some(b => isCurrentUser(b.bidder)) ? 'AuctionLost' : null;
}

/** Listing detail ("View Specs"): photos, title, fitment, details / specs, seller, the next-bid calculation, time left, bid history. */
export const PartDetailsScreen: React.FC<Props> = ({ route, navigation }) => {
  const money = useMoney();
  const sellerAccount = isSellerAccount(useAuth().user);
  const { country, location } = useLocations();
  const { auctionId, auction: initialAuction } = route.params;
  const paymentConfig = usePaymentConfig();
  const [listing, setListing] = useState<Listing | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [bids, setBids] = useState<Bid[]>([]);
  const [photo, setPhoto] = useState(0);
  const [noteOpen, setNoteOpen] = useState(false);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [quantity, setQuantity] = useState(1);
  const [reviews, setReviews] = useState<Review[]>([]);
  const [, setTick] = useState(0);
  // The next valid bid from BidService, recalculated live when someone bids.
  const { next, reload: reloadNext } = useNextBid(auctionId);

  const load = useCallback(async () => {
    try {
      setListing(await MarketApi.listingDetail(auctionId));
      setError(null);
    } catch (e) {
      setError(listingErrorMessage(e, "Couldn't load this listing."));
    }
    MarketApi.bidHistory(auctionId).then(setBids).catch(() => undefined);
    // Item reviews (sold items).
    ReviewApi.forListing(auctionId).then(setReviews).catch(() => undefined);
  }, [auctionId]);

  useEffect(() => {
    load();
  }, [load]);

  // Live: new bids and the end of the auction update this screen for everyone viewing it.
  const onAuctionEvent = useCallback(
    (m: { auctionId?: string }) => {
      if (m?.auctionId === auctionId) load();
    },
    [auctionId, load],
  );
  useLiveEvent('BidPlaced', onAuctionEvent);
  useLiveEvent('AuctionFinished', onAuctionEvent);

  // A bidder's result once the auction is sold: the existing "You won" / "Auction lost" screens.
  // (Fixed-price listings have buyers, not a winner.)
  const outcome = listing && listing.saleType !== 'FixedPrice' ? bidOutcome(listing, bids) : null;
  const openOutcome = useCallback(() => {
    if (!listing || !outcome) return;
    const sold = { ...mapRawDtoToAuction(listing), currentHighBid: listing.soldAmount ?? listing.currentHighBid ?? listing.startingPrice };
    if (outcome === 'AuctionWon') navigation.navigate('AuctionWon', { auction: sold });
    else navigation.navigate('AuctionLost', { auction: sold });
  }, [listing, outcome, navigation]);
  useEffect(() => {
    if (!route.params.showOutcome || !outcome) return;
    navigation.setParams({ showOutcome: undefined });
    openOutcome();
  }, [route.params.showOutcome, outcome, openOutcome, navigation]);

  // Countdown.
  useEffect(() => {
    const t = setInterval(() => setTick(x => x + 1), 1000);
    return () => clearInterval(t);
  }, []);

  // "Shipping to Kukatpally: ~₹350 (estimate)" for the buyer's selected location (not for the seller).
  const { quote: shippingQuote, place: shippingPlace, hasLocation } = useShippingEstimate(
    listing?.id,
    !!listing && listing.deliveryOptions.includes('Shipping') && !isCurrentUser(listing.seller),
  );

  if (!listing) {
    return (
      <SafeAreaView style={styles.container}>
        <Header title="Listing" onBack={() => navigation.goBack()} />
        {error ? <Text style={styles.error}>{error}</Text> : <ActivityIndicator color={COLORS.primaryGlow} style={styles.loader} />}
      </SafeAreaView>
    );
  }

  // The distance from the buyer's location and whether it's inside their radius (pickup offered to them).
  const distanceKm = initialAuction?.distanceKm ??
    (location && listing.latitude != null && listing.longitude != null
      ? Math.round(haversineKm(location.latitude, location.longitude, listing.latitude, listing.longitude) * 10) / 10
      : undefined);
  const withinRadius = location && distanceKm != null ? distanceKm <= location.radiusKm : undefined;
  const auction = { ...mapRawDtoToAuction(listing), distanceKm, withinRadius };
  const isSeller = isCurrentUser(listing.seller);
  const offersShipping = listing.deliveryOptions.includes('Shipping');
  const forBuyer = { ...listing, distanceKm, withinRadius, shippingQuote: offersShipping ? shippingQuote ?? undefined : null };
  const buyerDelivery = !isSeller ? buyerDeliveryText(forBuyer, money, country) : null;
  // Shipping can't be worked out for this buyer: Buy now / Place bid say why (and stop when there's no other way).
  const checkReceive = (go: () => void, action: string) => {
    const problem = isSeller ? null : receiveProblem(forBuyer, country);
    if (!problem) return go();
    if (problem.blocking) return Alert.alert(SHIPPING_NOT_AVAILABLE, problem.message);
    Alert.alert(SHIPPING_NOT_AVAILABLE, problem.message, [{ text: 'Cancel', style: 'cancel' }, { text: action, onPress: go }]);
  };
  // Editing, accepting bids and relisting need a seller account (the backend refuses buyers too).
  const canManage = isSeller && sellerAccount;
  // Fixed price: bought at the price until sold out – no bids, no end time.
  const isFixed = listing.saleType === 'FixedPrice';
  const available = listing.quantityAvailable ?? 0;
  const highBidder = isFixed ? undefined : next?.highBidder ?? bids[0]?.bidder;
  const isWinning = !!highBidder && isCurrentUser(highBidder);
  const currentTotal = isFixed ? listing.startingPrice : next?.currentTotal ?? listing.currentHighBid ?? listing.startingPrice;
  const bidCount = isFixed ? 0 : next?.bidCount ?? listing.bidCount;
  const isLive = listing.status === 'Live' && (isFixed ? available > 0 : new Date(listing.auctionEnd).getTime() > Date.now());
  const sellerName = listing.sellerName || listing.seller;
  const openSeller = () => navigation.navigate('SellerProfile', { seller: listing.seller, sellerName: listing.sellerName ?? undefined });
  const hasBids = bidCount > 0;
  const images = [...listing.imageItems].sort((a, b) => a.sortOrder - b.sortOrder);
  const d = listing.partDetails;
  const v = listing.vehicle;
  const highBid = bids.find(b => b.amount === next?.currentHighBid) ?? bids[0];

  const accept = () => {
    if (!highBid) return;
    Alert.alert(`Accept ${money(highBid.amount)} and close?`, `${maskUsername(highBid.bidder)} wins this auction now. This can't be undone.`, [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Accept and close',
        onPress: async () => {
          setBusy(true);
          try {
            await MarketApi.acceptBid(listing.id, highBid.id);
            Alert.alert('Sold', 'The sale is in your Orders: hand the item over to the buyer.');
            await load();
            reloadNext();
          } catch (e) {
            Alert.alert('Not accepted', e instanceof ServerError ? (e.status === 409 ? 'A higher bid just came in – check the new highest bid.' : e.message) : 'Please try again.');
            await load();
            reloadNext();
          } finally {
            setBusy(false);
          }
        },
      },
    ]);
  };

  const cancel = () =>
    Alert.alert('Cancel this listing?', 'It is removed from the marketplace. You can only cancel while there are no bids.', [
      { text: 'Keep it', style: 'cancel' },
      {
        text: 'Cancel listing',
        style: 'destructive',
        onPress: async () => {
          try {
            await MarketApi.cancelListing(listing.id);
            navigation.goBack();
          } catch (e) {
            Alert.alert('Not cancelled', e instanceof ServerError ? e.message : 'Please try again.');
          }
        },
      },
    ]);

  const relist = async () => {
    try {
      const copy = await MarketApi.relist(listing.id);
      navigation.replace('PartDetails', { auctionId: copy.id });
    } catch (e) {
      Alert.alert('Not relisted', e instanceof ServerError ? e.message : 'Please try again.');
    }
  };

  const qty = Math.min(Math.max(quantity, 1), Math.max(available, 1));
  // USA / Ireland: paid online (Stripe) before there is an order. India / Nigeria: as before ("coming soon").
  const online = paymentConfig.enabled && !!listing.countryCode && paymentConfig.countries.includes(listing.countryCode);
  const reserved = isFixed && available === 0 && (listing.quantityReserved ?? 0) > 0 && listing.status === 'Live';
  const checkout = () =>
    navigation.navigate('Checkout', {
      listingId: listing.id, title: listing.title ?? auction.title, imageUrl: images[0]?.thumbnailUrl ?? images[0]?.url ?? null,
      price: listing.startingPrice, quantity: qty, deliveryOptions: listing.deliveryOptions, countryCode: listing.countryCode!,
      pickupCity: listing.pickupCity ?? listing.locationName ?? null,
    });
  const buy = () =>
    Alert.alert(
      `Buy ${qty > 1 ? `${qty} × ` : ''}for ${money(listing.startingPrice * qty)}?`,
      [
        'The seller is told at once. You arrange payment and handover with them in your Orders.',
        'Online payment is coming soon in your country.',
        // Pickup is free; shipping is added at the exact, fixed charge when you choose it in Orders.
        [
          listing.deliveryOptions.includes('Pickup') ? 'Pickup: free.' : null,
          offersShipping
            ? shippingQuote?.available && shippingQuote.charge != null
              ? `Shipping: ~${money(shippingQuote.charge)}${shippingPlace ? ` to ${shippingPlace}` : ''} – the exact charge is shown when you choose it.`
              : 'Shipping: the charge is shown when you choose your delivery location.'
            : null,
        ].filter(Boolean).join(' '),
      ].filter(Boolean).join('\n\n'),
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Buy now',
          onPress: async () => {
            setBusy(true);
            try {
              await MarketApi.buy(listing.id, qty);
              setQuantity(1);
              await load();
              Alert.alert('Bought 🎉', 'Your order is in Orders – choose how to receive it there.', [
                { text: 'Later', style: 'cancel' },
                { text: 'View order', onPress: () => navigation.navigate('MainTabs', { screen: 'MyPurchases' }) },
              ]);
            } catch (e) {
              await load();
              Alert.alert('Not bought', listingErrorMessage(e, 'Please try again.'));
            } finally {
              setBusy(false);
            }
          },
        },
      ],
    );

  const saveNote = async () => {
    try {
      setListing(await MarketApi.addNote(listing.id, note));
      setNote('');
      setNoteOpen(false);
    } catch (e) {
      Alert.alert('Note not added', e instanceof ServerError ? e.message : 'Please try again.');
    }
  };

  return (
    <SafeAreaView style={styles.container}>
      <Header title={listing.kind === 'Vehicle' ? 'Vehicle' : 'Part Specifications'} onBack={() => navigation.goBack()} />

      <ScrollView contentContainerStyle={styles.scroll}>
        {/* Photos */}
        <View style={styles.imageBox}>
          <ScrollView horizontal pagingEnabled showsHorizontalScrollIndicator={false}
            onMomentumScrollEnd={e => setPhoto(Math.round(e.nativeEvent.contentOffset.x / (WIDTH - SPACING.md * 2)))}>
            {images.length > 0 ? images.map(img => (
              <Image key={img.id} source={{ uri: img.url }} style={styles.image} resizeMode="cover" />
            )) : <View style={styles.image} />}
          </ScrollView>
          <View style={styles.badgeWrap}>
            <Badge
              label={isLive ? (isFixed ? 'BUY NOW' : 'LIVE') : isFixed && listing.status === 'Sold' ? 'SOLD OUT' : label(listing.status).toUpperCase()}
              variant={isLive ? (isFixed ? 'success' : 'primary') : 'dark'}
            />
          </View>
          {images.length > 1 ? <Text style={styles.counter}>{photo + 1}/{images.length}{images[photo]?.angle ? ` · ${label(images[photo].angle)}` : ''}</Text> : null}
        </View>

        {/* Title, price, next bid */}
        <View style={styles.card}>
          <Text style={styles.title}>{listing.title}</Text>
          {listing.fitmentSummary ? <Text style={styles.fitment}>{listing.fitmentSummary}</Text> : null}
          {/* For a buyer: "📍 Pickup · 12 km away · 🚚 Shipping ~₹350" or "🚚 Ships to you ~₹1,200 · 640 km away". */}
          {buyerDelivery ? (
            <Text style={[styles.delivery, styles.deliveryBold, shippingQuote && !shippingQuote.available && styles.deliveryWarn]}
              testID="listing-delivery">
              {buyerDelivery}
            </Text>
          ) : null}
          {(isSeller || withinRadius === undefined) && deliverySummary(listing, money) ? <Text style={styles.delivery}>{deliverySummary(listing, money)}</Text> : null}
          {withinRadius === false && listing.deliveryOptions.includes('Pickup') && !isSeller ? (
            <Text style={styles.delivery}>📍 Pickup is possible in {listing.pickupCity ?? listing.locationName ?? 'the seller’s area'} – outside your {formatRadius(location!.radiusKm, country)} radius</Text>
          ) : null}
          {/* The shipping charge to the buyer's selected location (calculated by the backend). */}
          {offersShipping && !isSeller ? (
            <Text style={[styles.delivery, shippingQuote && !shippingQuote.available && styles.deliveryWarn]}>
              🚚 {shippingQuote ? shippingEstimateText(shippingQuote, shippingPlace, money)
                : hasLocation ? 'Working out the shipping charge…' : 'Set your location to see the shipping charge'}
            </Text>
          ) : null}
          <Text style={styles.seller}>
            Listed by:{' '}
            <Text style={[styles.sellerBold, styles.sellerLink]} onPress={openSeller} accessibilityRole="link">
              {isSeller ? 'You' : sellerName}
            </Text>
            <Text style={styles.rating} onPress={openSeller}>  {ratingText(listing.sellerRating, listing.sellerRatingCount)}</Text>
            {listing.locationName ? ` · 📍 ${listing.locationName}` : ''}
            {auction.distanceKm !== undefined ? ` · ${formatDistance(auction.distanceKm, country)} away` : ''}
          </Text>
          {/* Buyers ask the seller questions (not on your own listing). */}
          {!isSeller && listing.status !== 'Cancelled' ? (
            <TouchableOpacity style={styles.chatBtn} onPress={() => navigation.navigate('Conversation', { listingId: listing.id })}
              accessibilityRole="button">
              <Text style={styles.chatBtnText}>💬 Chat with seller</Text>
            </TouchableOpacity>
          ) : null}

          <View style={styles.priceStrip}>
            <View>
              <Text style={styles.priceLabel}>{isFixed ? 'Price' : hasBids ? 'Current bid' : 'Part price'}</Text>
              <Text style={styles.priceValue}>{money(currentTotal)}</Text>
              {hasBids ? <Text style={styles.small}>Part price {money(listing.startingPrice)}</Text> : null}
            </View>
            <View style={styles.rightCol}>
              {isFixed ? (
                <>
                  <View style={styles.bidBadge}>
                    <Text style={styles.bidText}>{available > 0 ? `${available} available` : reserved ? 'Reserved' : 'Sold out'}</Text>
                  </View>
                  <Text style={styles.timeLeft}>Direct sale · no bidding</Text>
                </>
              ) : (
                <>
                  <View style={styles.bidBadge}>
                    <Text style={styles.bidText}>{bidCount} {bidCount === 1 ? 'Bid' : 'Bids'}</Text>
                  </View>
                  <Countdown end={listing.auctionEnd} prefix="⏱ " style={[styles.timeLeft, styles.timer]} />
                </>
              )}
            </View>
          </View>

          {/* Current bid + step = next bid (from BidService) */}
          {next && isLive && !isFixed ? <NextBidCalculation next={next} /> : null}

          {isWinning && isLive ? (
            <View style={styles.winningBanner}>
              <Text style={styles.winningText}>✓ You're the highest bidder</Text>
            </View>
          ) : null}
          {!isLive && !isFixed && listing.winner && isCurrentUser(listing.winner) ? (
            <View style={styles.winningBanner}>
              <Text style={styles.winningText}>🎉 You won – {money(listing.soldAmount ?? currentTotal)}. See Orders.</Text>
            </View>
          ) : null}
        </View>

        {/* Logistics & Container Feasibility (hidden while FEATURES.containers is off) */}
        {FEATURES.containers ? (
          <View style={styles.card}>
            <Text style={styles.sectionHeading}>🚢 CONTAINER LOGISTICS & PORT DESTINATION</Text>
            <Row k="Assigned Container:" v={auction.containerNumber} />
            <Row k="Clearing Port:" v={auction.pickupPort} />
          </View>
        ) : null}

        {/* Fitment */}
        {listing.fitments.length > 0 ? (
          <View style={styles.card}>
            <Text style={styles.sectionHeading}>🚘 FITS THESE VEHICLES</Text>
            {listing.fitments.map((f, i) => (
              <View key={i} style={styles.fitRow}>
                <Text style={styles.fitTitle}>
                  {f.yearTo > f.yearFrom ? `${f.yearFrom}–${f.yearTo}` : f.yearFrom} {f.make} {f.model}
                </Text>
                <Text style={styles.fitMeta}>
                  {[f.trim, f.engine, f.fuel, f.transmission, f.drivetrain, f.steeringSide, f.market].filter(Boolean).join(' · ') || 'Any trim / engine'}
                  {f.note ? ` – ${f.note}` : ''}
                </Text>
              </View>
            ))}
          </View>
        ) : null}

        {/* Details / specs */}
        <View style={styles.card}>
          <Text style={styles.sectionHeading}>⚙️ {listing.kind === 'Vehicle' ? 'VEHICLE DETAILS' : 'PART DETAILS'}</Text>
          {listing.kind === 'Vehicle' ? (
            <>
              <Row k="Year / make / model" v={`${listing.year} ${listing.make} ${listing.model}`} />
              <Row k="Trim" v={v?.trim} />
              <Row k="Body" v={v?.bodyType} />
              <Row k="Fuel" v={v?.fuel} />
              <Row k="Engine" v={v?.engineSize ? `${v.engineSize} L` : null} />
              <Row k="Transmission" v={v?.transmission} />
              <Row k="Drivetrain" v={v?.drivetrain} />
              <Row k="Steering" v={v?.steeringSide} />
              <Row k="Colour" v={listing.color} />
              <Row k="Mileage" v={listing.mileage != null ? `${listing.mileage.toLocaleString()} ${v?.mileageUnit ?? 'km'}` : null} />
              <Row k="Running" v={label(v?.runningStatus)} />
              <Row k="Damage" v={[label(v?.damageType), ...(v?.damageAreas ?? [])].filter(Boolean).join(', ')} />
              <Row k="Keys / owners" v={v ? [v.keys != null ? `${v.keys} keys` : null, v.owners != null ? `${v.owners} owners` : null].filter(Boolean).join(' · ') : null} />
              <Row k="Service history" v={v?.serviceHistory} />
              <Row k="VIN" v={v?.vin} />
              <Row k="Inspection" v={v ? (v.inspectionAllowed ? 'Allowed before bidding' : 'Not offered') : null} />
              {v ? Object.entries(v.papers).map(([k, val]) => <Row key={k} k={label(k)} v={val} />) : null}
            </>
          ) : (
            <>
              <Row k="Part type" v={[listing.category, listing.subCategory, listing.partType].filter(Boolean).join(' › ')} />
              <Row k="Fits" v={label(listing.fitmentMode)} />
              {Object.entries(listing.specs).map(([k, val]) => <Row key={k} k={label(k)} v={val} />)}
              <Row k="Brand" v={[label(d?.brandType), d?.brand].filter(Boolean).join(' · ')} />
              <Row k="OEM numbers" v={listing.partNumberItems.filter(n => n.kind !== 'Aftermarket').map(n => n.value).join(', ')} />
              <Row k="Interchange" v={listing.partNumberItems.filter(n => n.kind === 'Aftermarket').map(n => n.value).join(', ')} />
              <Row k="Position" v={[d?.position, d?.side].filter(Boolean).join(' ')} />
              <Row k="Condition" v={[label(d?.condition), d?.usedGrade ? `Grade ${d.usedGrade}` : null].filter(Boolean).join(' · ')} />
              <Row k="Donor mileage" v={d?.donorMileage ? d.donorMileage.toLocaleString() : null} />
              <Row k="Colour" v={[d?.colour, d?.paintCode].filter(Boolean).join(' · ')} />
              <Row k="Quantity" v={d?.quantity} />
              <Row k="Includes" v={d?.includes} />
              <Row k="Warranty / returns" v={d?.warranty} />
            </>
          )}
          <Row k="Pickup" v={listing.deliveryOptions.includes('Pickup') ? `Free – in ${listing.pickupCity ?? listing.locationName ?? 'the seller’s area'}` : 'Not offered'} />
          <Row
            k="Shipping"
            v={offersShipping
              ? [shippingQuote?.available && shippingQuote.charge != null ? `~${money(shippingQuote.charge)} to ${shippingPlace ?? 'you'} (estimate)` : 'Calculated from size and distance',
                shipsWithinText(listing.shipsWithin ?? listing.countryCode)].join(' · ')
              : 'Not offered'}
          />
          <Row k="Size class" v={listing.deliveryOptions.includes('Shipping') ? listing.sizeClass : null} />
          {isSeller && listing.pickupAddress ? <Row k="Pickup address (private)" v={listing.pickupAddress} /> : null}
          {listing.description ? (
            <>
              <Text style={styles.descTitle}>Description</Text>
              <Text style={styles.descText}>{listing.description}</Text>
            </>
          ) : null}
        </View>

        {/* Reviews of this item (after the sale) */}
        {reviews.length ? (
          <View style={styles.card}>
            <Text style={styles.sectionHeading}>⭐ REVIEWS OF THIS ITEM</Text>
            {reviews.map(r => (
              <ReviewCard key={r.id} review={r} onChange={u => setReviews(list => list.map(x => (x.id === u.id ? u : x)))} />
            ))}
          </View>
        ) : null}

        {/* Bid history (auctions only) */}
        {!isFixed ? (
          <View style={styles.card}>
            <Text style={styles.sectionHeading}>⚡ BID HISTORY</Text>
            {bids.length === 0 ? <Text style={styles.descText}>No bids yet.</Text> : null}
            {bids.map(b => (
              <View key={b.id} style={styles.infoRow}>
                <Text style={styles.infoKey}>{isCurrentUser(b.bidder) ? 'You' : maskUsername(b.bidder)}</Text>
                <Text style={styles.infoValue}>{money(b.amount)}</Text>
                <Text style={styles.bidTime}>{new Date(b.bidTime).toLocaleString()}</Text>
              </View>
            ))}
          </View>
        ) : null}

        {/* Seller tools */}
        {canManage && (listing.status === 'Live' || listing.status === 'Draft') ? (
          <View style={styles.card}>
            <Text style={styles.sectionHeading}>YOUR LISTING</Text>
            {listing.canEdit ? (
              <TouchableOpacity onPress={() => navigation.navigate('SellWizard', { listingId: listing.id })}>
                <Text style={styles.toolLink}>✎ Edit listing</Text>
              </TouchableOpacity>
            ) : (
              <Text style={styles.descText}>After the first bid you can only add photos and description notes.</Text>
            )}
            <TouchableOpacity onPress={() => navigation.navigate('SellWizard', { listingId: listing.id, step: 'photos' })}>
              <Text style={styles.toolLink}>📷 Add photos</Text>
            </TouchableOpacity>
            <TouchableOpacity onPress={() => setNoteOpen(true)}>
              <Text style={styles.toolLink}>📝 Add a description note</Text>
            </TouchableOpacity>
            {!hasBids && listing.status === 'Live' ? (
              <TouchableOpacity onPress={cancel}>
                <Text style={[styles.toolLink, styles.danger]}>✕ Cancel listing</Text>
              </TouchableOpacity>
            ) : null}
          </View>
        ) : null}
      </ScrollView>

      {/* Fixed Bottom Action Bar */}
      <View style={styles.bottomBar}>
        {isSeller ? (
          canManage && isLive && hasBids && highBid ? (
            <TouchableOpacity style={styles.bidBarBtn} onPress={accept} disabled={busy}>
              <Text style={styles.bidBarText}>ACCEPT {money(highBid.amount)} AND CLOSE</Text>
            </TouchableOpacity>
          ) : canManage && listing.status === 'Unsold' ? (
            <TouchableOpacity style={styles.bidBarBtn} onPress={relist}>
              <Text style={styles.bidBarText}>ENDED – NO BIDS · RELIST</Text>
            </TouchableOpacity>
          ) : (
            <Text style={styles.ownNote}>
              {isFixed && isLive
                ? `This is your listing – ${listing.quantitySold ?? 0} sold, ${available} left.`
                : isLive ? "This is your listing – you can't bid on it." : `This listing is ${label(listing.status).toLowerCase()}.`}
            </Text>
          )
        ) : reserved ? (
          // Another buyer is paying for the last units (15 minutes); free again if they don't.
          <View style={[styles.bidBarBtn, styles.reservedBar]} testID="listing-reserved">
            <Text style={styles.bidBarText}>RESERVED – ANOTHER BUYER IS PAYING · CHECK AGAIN SOON</Text>
          </View>
        ) : isLive && isFixed ? (
          <View style={styles.buyRow}>
            {available > 1 ? (
              <View style={styles.stepper}>
                <TouchableOpacity style={styles.stepBtn} onPress={() => setQuantity(Math.max(qty - 1, 1))} disabled={qty <= 1}
                  accessibilityLabel="One less">
                  <Text style={[styles.stepText, qty <= 1 && styles.stepDisabled]}>−</Text>
                </TouchableOpacity>
                <Text style={styles.stepValue} accessibilityLabel={`Quantity ${qty}`}>{qty}</Text>
                <TouchableOpacity style={styles.stepBtn} onPress={() => setQuantity(Math.min(qty + 1, available))} disabled={qty >= available}
                  accessibilityLabel="One more">
                  <Text style={[styles.stepText, qty >= available && styles.stepDisabled]}>+</Text>
                </TouchableOpacity>
              </View>
            ) : null}
            <TouchableOpacity style={[styles.bidBarBtn, styles.buyBarBtn]} onPress={() => checkReceive(online ? checkout : buy, 'Buy anyway')} disabled={busy}>
              <Text style={styles.bidBarText}>BUY NOW – {money(listing.startingPrice * qty)}</Text>
            </TouchableOpacity>
          </View>
        ) : isLive ? (
          isWinning ? (
            <View style={[styles.bidBarBtn, styles.winningBar]}>
              <Text style={styles.bidBarText}>YOU'RE THE HIGHEST BIDDER</Text>
            </View>
          ) : (
            <TouchableOpacity style={styles.bidBarBtn}
              onPress={() => checkReceive(() => navigation.navigate('PlaceBidModal', { auction: { ...auction, currentHighBid: currentTotal } }), 'Bid anyway')}>
              <Text style={styles.bidBarText}>{next ? `PLACE BID (${money(next.minimumBid)})` : 'PLACE BID'}</Text>
            </TouchableOpacity>
          )
        ) : outcome ? (
          <TouchableOpacity
            style={[styles.bidBarBtn, outcome === 'AuctionWon' && styles.winningBar]}
            onPress={openOutcome}
          >
            <Text style={styles.bidBarText}>{outcome === 'AuctionWon' ? 'YOU WON 🎉 · VIEW' : 'AUCTION LOST · VIEW'}</Text>
          </TouchableOpacity>
        ) : (
          <TouchableOpacity
            style={styles.requestAltBtn}
            onPress={() => navigation.navigate('OrderPartRequest', {
              prefillMake: listing.make || undefined,
              prefillModel: listing.model || undefined,
              prefillPart: listing.partType ?? listing.title ?? undefined,
            })}
          >
            <Text style={styles.requestAltText}>{isFixed ? 'SOLD OUT' : 'AUCTION ENDED'} — CREATE A CUSTOM ORDER FOR THIS PART</Text>
          </TouchableOpacity>
        )}
      </View>

      <Modal visible={noteOpen} transparent animationType="fade" onRequestClose={() => setNoteOpen(false)}>
        <View style={styles.noteBackdrop}>
          <View style={styles.noteBox}>
            <Text style={styles.sectionHeading}>ADD A DESCRIPTION NOTE</Text>
            <TextInput style={styles.noteInput} multiline value={note} onChangeText={setNote} maxLength={500}
              placeholder="e.g. Also includes the mounting clips" placeholderTextColor={COLORS.textMuted} />
            <View style={styles.noteActions}>
              <TouchableOpacity onPress={() => setNoteOpen(false)}><Text style={styles.toolLink}>Cancel</Text></TouchableOpacity>
              <TouchableOpacity onPress={saveNote} disabled={!note.trim()}><Text style={styles.toolLink}>Add note</Text></TouchableOpacity>
            </View>
          </View>
        </View>
      </Modal>
    </SafeAreaView>
  );
};

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: COLORS.background },
  loader: { marginTop: SPACING.xxl },
  error: { color: COLORS.danger, padding: SPACING.md },
  scroll: { padding: SPACING.md, paddingBottom: 100 },
  imageBox: { height: 240, borderRadius: RADIUS.lg, overflow: 'hidden', backgroundColor: COLORS.card, marginBottom: SPACING.md },
  image: { width: WIDTH - SPACING.md * 2, height: 240, backgroundColor: COLORS.card },
  badgeWrap: { position: 'absolute', top: SPACING.md, left: SPACING.md },
  counter: {
    position: 'absolute', bottom: SPACING.sm, right: SPACING.sm, color: COLORS.text, fontSize: 12, fontWeight: '700',
    backgroundColor: 'rgba(0,0,0,0.6)', paddingHorizontal: 8, paddingVertical: 2, borderRadius: RADIUS.sm,
  },
  card: { backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, padding: SPACING.md, borderWidth: 1, borderColor: COLORS.border, marginBottom: SPACING.md },
  title: { color: COLORS.text, fontSize: 18, fontWeight: '800', lineHeight: 24 },
  fitment: { color: COLORS.badgeText, fontSize: 13, fontWeight: '700', marginTop: 4 },
  delivery: { color: COLORS.textSecondary, fontSize: 13, fontWeight: '700', marginTop: 4 },
  deliveryWarn: { color: COLORS.warningGlow },
  deliveryBold: { color: COLORS.text },
  seller: { color: COLORS.textMuted, fontSize: 13, marginTop: 4, marginBottom: SPACING.md },
  sellerBold: { color: COLORS.textSecondary, fontWeight: '700' },
  chatBtn: {
    alignSelf: 'flex-start', backgroundColor: COLORS.cardSecondary, borderWidth: 1, borderColor: COLORS.primaryGlow,
    borderRadius: RADIUS.full, paddingHorizontal: SPACING.md, paddingVertical: SPACING.xs + 2, marginTop: -SPACING.sm, marginBottom: SPACING.md,
  },
  chatBtnText: { color: COLORS.primaryGlow, fontSize: 13, fontWeight: '800' },
  rating: { color: COLORS.warningGlow, fontWeight: '700' },
  sellerLink: { color: COLORS.primaryGlow, textDecorationLine: 'underline' },
  timer: { fontVariant: ['tabular-nums'] },
  buyRow: { flexDirection: 'row', alignItems: 'center', gap: SPACING.sm },
  buyBarBtn: { flex: 1, backgroundColor: COLORS.success },
  stepper: { flexDirection: 'row', alignItems: 'center', backgroundColor: COLORS.card, borderRadius: RADIUS.md, borderWidth: 1, borderColor: COLORS.border },
  stepBtn: { paddingHorizontal: SPACING.md, paddingVertical: 12 },
  stepText: { color: COLORS.text, fontSize: 18, fontWeight: '900' },
  stepDisabled: { color: COLORS.textMuted },
  stepValue: { color: COLORS.text, fontSize: 16, fontWeight: '800', minWidth: 24, textAlign: 'center' },
  priceStrip: {
    flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', backgroundColor: COLORS.card,
    padding: SPACING.md, borderRadius: RADIUS.md, borderWidth: 1, borderColor: COLORS.borderHighlight,
  },
  priceLabel: { color: COLORS.textMuted, fontSize: 11, textTransform: 'uppercase' },
  priceValue: { color: COLORS.primaryGlow, fontSize: 24, fontWeight: '900', marginTop: 2 },
  small: { color: COLORS.textMuted, fontSize: 11 },
  rightCol: { alignItems: 'flex-end', gap: 6 },
  bidBadge: { backgroundColor: COLORS.badgeBg, paddingHorizontal: SPACING.md, paddingVertical: SPACING.xs, borderRadius: RADIUS.full, borderWidth: 1, borderColor: COLORS.primary },
  bidText: { color: COLORS.primaryGlow, fontWeight: '700', fontSize: 12 },
  timeLeft: { color: COLORS.textSecondary, fontSize: 12, fontWeight: '700' },
  winningBanner: { backgroundColor: 'rgba(34, 164, 71, 0.15)', padding: SPACING.md, borderRadius: RADIUS.md, borderWidth: 1, borderColor: COLORS.success, marginTop: SPACING.md },
  winningText: { color: COLORS.success, fontWeight: '800', fontSize: 13 },
  sectionHeading: { color: COLORS.primaryGlow, fontSize: 12, fontWeight: '800', letterSpacing: 1, marginBottom: SPACING.sm },
  infoRow: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 6, borderBottomWidth: 1, borderBottomColor: COLORS.border, gap: SPACING.md },
  infoKey: { color: COLORS.textMuted, fontSize: 13 },
  infoValue: { color: COLORS.text, fontSize: 13, fontWeight: '700', flexShrink: 1, textAlign: 'right' },
  bidTime: { color: COLORS.textMuted, fontSize: 11 },
  fitRow: { paddingVertical: 6, borderBottomWidth: 1, borderBottomColor: COLORS.border },
  fitTitle: { color: COLORS.text, fontSize: 14, fontWeight: '700' },
  fitMeta: { color: COLORS.textMuted, fontSize: 12, marginTop: 2 },
  descTitle: { color: COLORS.textSecondary, fontSize: 13, fontWeight: '700', marginTop: SPACING.md, marginBottom: 4 },
  descText: { color: COLORS.textMuted, fontSize: 13, lineHeight: 18 },
  toolLink: { color: COLORS.primaryGlow, fontSize: 14, fontWeight: '800', paddingVertical: 6 },
  danger: { color: COLORS.danger },
  bottomBar: { position: 'absolute', bottom: 0, left: 0, right: 0, backgroundColor: COLORS.surface, padding: SPACING.md, borderTopWidth: 1, borderTopColor: COLORS.border },
  bidBarBtn: { backgroundColor: COLORS.primary, paddingVertical: 14, borderRadius: RADIUS.md, alignItems: 'center' },
  winningBar: { backgroundColor: COLORS.success },
  reservedBar: { backgroundColor: COLORS.cardSecondary },
  bidBarText: { color: COLORS.text, fontSize: 15, fontWeight: '900', letterSpacing: 1 },
  ownNote: { color: COLORS.textMuted, fontSize: 13, textAlign: 'center', paddingVertical: 8 },
  requestAltBtn: { backgroundColor: COLORS.cardSecondary, paddingVertical: 14, borderRadius: RADIUS.md, alignItems: 'center', borderWidth: 1, borderColor: COLORS.primary },
  requestAltText: { color: COLORS.primaryGlow, fontSize: 12, fontWeight: '800', letterSpacing: 0.5 },
  noteBackdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.6)', justifyContent: 'center', padding: SPACING.lg },
  noteBox: { backgroundColor: COLORS.surface, borderRadius: RADIUS.lg, padding: SPACING.md },
  noteInput: { backgroundColor: COLORS.card, color: COLORS.text, borderRadius: RADIUS.md, minHeight: 90, padding: SPACING.sm, textAlignVertical: 'top' },
  noteActions: { flexDirection: 'row', justifyContent: 'space-between', marginTop: SPACING.sm },
});
