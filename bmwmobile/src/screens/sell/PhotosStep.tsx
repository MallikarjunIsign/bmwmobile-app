import React, { useState } from 'react';
import { View, Text, StyleSheet, TouchableOpacity, Image, Alert } from 'react-native';
import { launchCamera, launchImageLibrary, type Asset, type ImageLibraryOptions } from 'react-native-image-picker';
import { COLORS, SPACING, RADIUS } from '../../theme/colors';
import { MarketApi, type LocalPhoto } from '../../services/marketApi';
import { ServerError } from '../../services/api';
import type { Listing, ListingImage } from '../../types/listing';
import { label } from '../../utils/time';
import { SectionTitle, FieldError } from './sellUi';

/** Limits (same as the server): parts 3–10, vehicles 8–20; max 5 MB each after compression. */
export const PHOTO_LIMITS = { part: { min: 3, max: 10 }, vehicle: { min: 8, max: 20 } };
export const MAX_PHOTO_BYTES = 5 * 1024 * 1024;
export const VEHICLE_ANGLES = ['Front', 'Rear', 'Left', 'Right', 'Interior', 'Dashboard', 'EngineBay', 'Damage'] as const;

/** Resized on the phone to max 1600 px and compressed (JPEG ~80%) before upload. */
export const PICKER: ImageLibraryOptions = {
  mediaType: 'photo',
  maxWidth: 1600,
  maxHeight: 1600,
  quality: 0.8,
  includeExtra: false,
};

const toLocal = (a: Asset): LocalPhoto => ({ uri: a.uri!, type: a.type ?? 'image/jpeg', fileName: a.fileName ?? undefined });

interface PhotosStepProps {
  listing: Listing;
  onChange: (images: ListingImage[]) => void;
  errors?: string[];
}

export const PhotosStep: React.FC<PhotosStepProps> = ({ listing, onChange, errors }) => {
  const isVehicle = listing.kind === 'Vehicle';
  const limits = isVehicle ? PHOTO_LIMITS.vehicle : PHOTO_LIMITS.part;
  const images = [...listing.imageItems].sort((a, b) => a.sortOrder - b.sortOrder);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const hasBids = listing.bidCount > 0;
  const remaining = limits.max - images.length;

  const upload = async (assets: Asset[], angle?: string) => {
    const ok = assets.filter(a => a.uri);
    const tooBig = ok.filter(a => (a.fileSize ?? 0) > MAX_PHOTO_BYTES);
    if (tooBig.length > 0) {
      setError('Some photos are larger than 5 MB even after compression – please pick smaller ones.');
      return;
    }
    if (ok.length === 0) return;
    setError(null);
    setProgress(0);
    try {
      const updated = await MarketApi.uploadPhotos(listing.id, ok.map(toLocal), setProgress, angle);
      onChange(updated);
    } catch (e) {
      setError(e instanceof ServerError ? e.message : "Couldn't upload the photos.");
    } finally {
      setProgress(null);
    }
  };

  const pick = (angle?: string) => {
    if (remaining <= 0) {
      setError(`You can add at most ${limits.max} photos.`);
      return;
    }
    Alert.alert(angle ? `${label(angle)} photo` : 'Add photos', undefined, [
      {
        text: 'Camera',
        onPress: async () => {
          const res = await launchCamera({ ...PICKER, saveToPhotos: false });
          if (!res.didCancel && res.assets) await upload(res.assets, angle);
          else if (res.errorMessage) setError(res.errorMessage);
        },
      },
      {
        text: 'Gallery',
        onPress: async () => {
          const res = await launchImageLibrary({ ...PICKER, selectionLimit: angle ? 1 : remaining });
          if (!res.didCancel && res.assets) await upload(res.assets, angle);
          else if (res.errorMessage) setError(res.errorMessage);
        },
      },
      { text: 'Cancel', style: 'cancel' },
    ]);
  };

  const remove = (image: ListingImage) =>
    Alert.alert('Delete this photo?', undefined, [
      { text: 'Keep', style: 'cancel' },
      {
        text: 'Delete',
        style: 'destructive',
        onPress: async () => {
          try {
            await MarketApi.deletePhoto(listing.id, image.id);
            onChange(images.filter(i => i.id !== image.id).map((i, n) => ({ ...i, sortOrder: n, isPrimary: n === 0 })));
          } catch (e) {
            setError(e instanceof ServerError ? e.message : "Couldn't delete the photo.");
          }
        },
      },
    ]);

  /** Moves a photo; the first one is the cover. */
  const move = async (index: number, delta: number) => {
    const target = index + delta;
    if (target < 0 || target >= images.length) return;
    const ids = images.map(i => i.id);
    [ids[index], ids[target]] = [ids[target], ids[index]];
    try {
      onChange(await MarketApi.reorderPhotos(listing.id, ids));
    } catch (e) {
      setError(e instanceof ServerError ? e.message : "Couldn't reorder the photos.");
    }
  };

  return (
    <View>
      <SectionTitle
        title="Photos"
        hint={`${limits.min}–${limits.max} photos, JPG / PNG / WEBP. The first photo is the cover. ${
          isVehicle ? 'Follow the guided angles; add damage close-ups if there is damage.' : 'Show the front, the back, the label / part number and any damage.'
        }${hasBids ? ' After the first bid you can only add photos.' : ''}`}
      />

      {isVehicle ? (
        <View style={styles.angles}>
          {VEHICLE_ANGLES.map(angle => {
            const photo = images.find(i => i.angle === angle);
            return (
              <TouchableOpacity key={angle} style={styles.angle} onPress={() => !photo && pick(angle)} activeOpacity={0.8}>
                {photo ? (
                  <Image source={{ uri: photo.thumbnailUrl ?? photo.url }} style={styles.angleImage} />
                ) : (
                  <Text style={styles.plus}>＋</Text>
                )}
                <Text style={[styles.angleLabel, photo && styles.angleDone]}>{photo ? '✓ ' : ''}{label(angle)}</Text>
              </TouchableOpacity>
            );
          })}
        </View>
      ) : null}

      <View style={styles.grid}>
        {images.map((image, index) => (
          <View key={image.id} style={styles.tile}>
            <Image source={{ uri: image.thumbnailUrl ?? image.url }} style={styles.thumb} />
            {index === 0 ? <Text style={styles.cover}>COVER</Text> : null}
            {image.angle ? <Text style={styles.angleTag}>{label(image.angle)}</Text> : null}
            {!hasBids ? (
              <View style={styles.tileActions}>
                <TouchableOpacity onPress={() => move(index, -1)} disabled={index === 0} hitSlop={6} accessibilityLabel="Move left">
                  <Text style={[styles.action, index === 0 && styles.disabled]}>◀</Text>
                </TouchableOpacity>
                <TouchableOpacity onPress={() => remove(image)} hitSlop={6} accessibilityLabel="Delete photo">
                  <Text style={styles.delete}>✕</Text>
                </TouchableOpacity>
                <TouchableOpacity onPress={() => move(index, 1)} disabled={index === images.length - 1} hitSlop={6} accessibilityLabel="Move right">
                  <Text style={[styles.action, index === images.length - 1 && styles.disabled]}>▶</Text>
                </TouchableOpacity>
              </View>
            ) : null}
          </View>
        ))}
        {remaining > 0 ? (
          <TouchableOpacity style={[styles.tile, styles.addTile]} onPress={() => pick()} activeOpacity={0.8}>
            <Text style={styles.plus}>＋</Text>
            <Text style={styles.addText}>{isVehicle ? 'More photos' : 'Add photos'}</Text>
          </TouchableOpacity>
        ) : null}
      </View>

      <Text style={styles.count}>
        {images.length} / {limits.max} photos{images.length < limits.min ? ` – add at least ${limits.min - images.length} more` : ''}
      </Text>

      {progress !== null ? (
        <View style={styles.progressWrap} accessibilityLabel="upload-progress">
          <View style={[styles.progressBar, { width: `${Math.round(progress * 100)}%` }]} />
          <Text style={styles.progressText}>Uploading… {Math.round(progress * 100)}%</Text>
        </View>
      ) : null}
      {error ? <Text style={styles.error}>{error}</Text> : null}
      <FieldError messages={errors} />
    </View>
  );
};

const TILE = 100;

const styles = StyleSheet.create({
  angles: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: SPACING.xs,
    marginTop: SPACING.md,
  },
  angle: {
    width: '23.5%',
    aspectRatio: 0.85,
    backgroundColor: COLORS.card,
    borderRadius: RADIUS.md,
    borderWidth: 1,
    borderColor: COLORS.border,
    alignItems: 'center',
    justifyContent: 'center',
    overflow: 'hidden',
  },
  angleImage: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    opacity: 0.6,
  },
  angleLabel: {
    color: COLORS.textSecondary,
    fontSize: 11,
    fontWeight: '700',
    marginTop: 4,
  },
  angleDone: {
    color: COLORS.successGlow,
  },
  grid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: SPACING.sm,
    marginTop: SPACING.md,
  },
  tile: {
    width: TILE,
    height: TILE,
    borderRadius: RADIUS.md,
    overflow: 'hidden',
    backgroundColor: COLORS.card,
  },
  thumb: {
    width: '100%',
    height: '100%',
  },
  cover: {
    position: 'absolute',
    top: 4,
    left: 4,
    backgroundColor: COLORS.primary,
    color: COLORS.text,
    fontSize: 9,
    fontWeight: '900',
    paddingHorizontal: 4,
    borderRadius: 4,
  },
  angleTag: {
    position: 'absolute',
    top: 4,
    right: 4,
    color: COLORS.text,
    fontSize: 9,
    fontWeight: '800',
    backgroundColor: 'rgba(0,0,0,0.6)',
    paddingHorizontal: 4,
    borderRadius: 4,
  },
  tileActions: {
    position: 'absolute',
    bottom: 0,
    left: 0,
    right: 0,
    flexDirection: 'row',
    justifyContent: 'space-around',
    backgroundColor: 'rgba(0,0,0,0.55)',
    paddingVertical: 3,
  },
  action: {
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '900',
  },
  disabled: {
    opacity: 0.3,
  },
  delete: {
    color: COLORS.danger,
    fontSize: 15,
    fontWeight: '900',
  },
  addTile: {
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 1,
    borderColor: COLORS.primary,
    borderStyle: 'dashed',
  },
  plus: {
    color: COLORS.primaryGlow,
    fontSize: 26,
    fontWeight: '700',
  },
  addText: {
    color: COLORS.primaryGlow,
    fontSize: 11,
    fontWeight: '700',
  },
  count: {
    color: COLORS.textSecondary,
    fontSize: 12,
    marginTop: SPACING.sm,
  },
  progressWrap: {
    marginTop: SPACING.sm,
    height: 22,
    borderRadius: RADIUS.sm,
    backgroundColor: COLORS.card,
    overflow: 'hidden',
    justifyContent: 'center',
  },
  progressBar: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    backgroundColor: COLORS.primary,
  },
  progressText: {
    color: COLORS.text,
    fontSize: 11,
    fontWeight: '700',
    textAlign: 'center',
  },
  error: {
    color: COLORS.danger,
    fontSize: 12,
    fontWeight: '600',
    marginTop: SPACING.xs,
  },
});
