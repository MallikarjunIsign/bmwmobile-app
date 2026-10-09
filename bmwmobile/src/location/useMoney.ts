import { useCallback } from 'react';
import { useLocations } from './LocationContext';
import { formatMoney, REGIONS } from '../utils/geo';

/**
 * Prices in the currency of the selected location's country: ₹ India, $ USA, ₦ Nigeria, € Ireland.
 * Only the symbol and number format change — amounts are not converted.
 */
export function useMoney() {
  const { country } = useLocations();
  return useCallback((amount: number, decimals = 0) => formatMoney(amount, country, decimals), [country]);
}

/** "₹ INR", "$ USD", ... for labels such as "YOUR BID AMOUNT (₹ INR)". */
export function useCurrencyLabel() {
  const { country } = useLocations();
  const r = REGIONS[country];
  return `${r.currencySymbol} ${r.currencyCode}`;
}
