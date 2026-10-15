import React from 'react';
import { SearchBox, rememberSearch } from '../components/SearchBox';
import { useLocations } from '../location/LocationContext';
import { useBrowse } from './BrowseContext';
import type { QueryChip } from '../types/listing';

/**
 * The search box on Home and Market, wired to the ONE shared search state: the same text and chips on both screens.
 * A suggestion / recent search applies its chips, the keyboard's search key searches the text, both with every
 * active filter; the results on both screens update at once.
 */
export const BrowseSearchBox: React.FC = () => {
  const { country } = useLocations();
  const browse = useBrowse();
  const { chips, draftText, itemType } = browse;

  const pick = (picked: QueryChip[], text = '') => {
    browse.applyChips(picked, text);
    const kept = chips.filter(c => !picked.some(p => p.type === c.type));
    rememberSearch({ text, chips: [...kept, ...picked] });
  };

  const submit = () => {
    browse.submitText();
    rememberSearch({ text: draftText, chips });
  };

  return (
    <SearchBox
      text={draftText}
      chips={chips}
      onChangeText={browse.setDraftText}
      onPick={pick}
      onRemoveChip={browse.removeChip}
      onSubmit={submit}
      onClear={browse.clearSearch}
      country={country}
      itemType={itemType}
    />
  );
};
