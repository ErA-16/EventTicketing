import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  apiRequest,
  errorMessage,
  isApiError,
} from '../../api/client';
import type {
  EventResponse,
  ImageUploadResponse,
  TicketType,
  TicketTypeInput,
} from '../../api/types';
import { ErrorState, Loading } from '../../components/Feedback';
import { ArrowIcon } from '../../components/Icons';
import { StatusPill } from '../../components/StatusPill';
import { useToast } from '../../context/ToastContext';
import {
  defaultSalesOpenInput,
  defaultStartsAtInput,
  fromLocalInputValue,
  toLocalInputValue,
} from '../../lib/format';

interface TierDraft {
  key: string;
  id?: string;
  name: string;
  price: string;
  quantity: string;
  salesOpenAt: string;
  earlyBirdPrice: string;
  earlyBirdEndsAt: string;
}

const MAX_IMAGE_BYTES = 2_621_440;

function newTier(): TierDraft {
  return {
    key: crypto.randomUUID(),
    name: '',
    price: '',
    quantity: '100',
    salesOpenAt: defaultSalesOpenInput(),
    earlyBirdPrice: '',
    earlyBirdEndsAt: '',
  };
}

function toTierDraft(tier: TicketType): TierDraft {
  return {
    key: tier.id,
    id: tier.id,
    name: tier.name,
    price: String(tier.price),
    quantity: String(tier.quantity),
    salesOpenAt: toLocalInputValue(tier.salesOpenAt),
    earlyBirdPrice: tier.earlyBirdPrice != null ? String(tier.earlyBirdPrice) : '',
    earlyBirdEndsAt: tier.earlyBirdEndsAt
      ? toLocalInputValue(tier.earlyBirdEndsAt)
      : '',
  };
}

function tierPayload(tier: TierDraft): TicketTypeInput {
  return {
    name: tier.name.trim(),
    price: Number(tier.price),
    quantity: Number(tier.quantity),
    salesOpenAt: fromLocalInputValue(tier.salesOpenAt),
    earlyBirdPrice: tier.earlyBirdPrice.trim()
      ? Number(tier.earlyBirdPrice)
      : null,
    earlyBirdEndsAt: tier.earlyBirdEndsAt
      ? fromLocalInputValue(tier.earlyBirdEndsAt)
      : null,
  };
}

export function OrganizerEventFormPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const toast = useToast();
  const isEditRoute = Boolean(id);

  const [eventId, setEventId] = useState<string | null>(id ?? null);
  const [sourceEvent, setSourceEvent] = useState<EventResponse | null>(null);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [venue, setVenue] = useState('');
  const [startsAtInput, setStartsAtInput] = useState(defaultStartsAtInput());
  const [tiers, setTiers] = useState<TierDraft[]>(() => [newTier()]);
  const [removedIds, setRemovedIds] = useState<string[]>([]);
  const [imageFile, setImageFile] = useState<File | null>(null);
  const [imagePreview, setImagePreview] = useState<string | null>(null);
  const [existingImageUrl, setExistingImageUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(isEditRoute);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [details, setDetails] = useState<string[]>([]);
  const [pendingAction, setPendingAction] = useState<'draft' | 'submit' | null>(
    null,
  );

  const load = useCallback(async () => {
    if (!id) {
      return;
    }

    setLoading(true);
    setLoadError(null);
    try {
      const event = await apiRequest<EventResponse>(`/organizer/events/${id}`);
      setSourceEvent(event);
      setEventId(event.id);
      setTitle(event.title);
      setDescription(event.description);
      setVenue(event.venue);
      setStartsAtInput(toLocalInputValue(event.startsAt));
      setTiers(event.ticketTypes.map(toTierDraft));
      setExistingImageUrl(event.imageUrl);
    } catch (caught) {
      setLoadError(errorMessage(caught, 'Could not load this event.'));
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    if (!imageFile) {
      setImagePreview(null);
      return;
    }

    const url = URL.createObjectURL(imageFile);
    setImagePreview(url);
    return () => URL.revokeObjectURL(url);
  }, [imageFile]);

  const updateTier = (key: string, patch: Partial<TierDraft>) => {
    setTiers((current) =>
      current.map((tier) => (tier.key === key ? { ...tier, ...patch } : tier)),
    );
  };

  const removeTier = (tier: TierDraft) => {
    if (tier.id) {
      setRemovedIds((current) =>
        current.includes(tier.id!) ? current : [...current, tier.id!],
      );
    }
    setTiers((current) => current.filter((item) => item.key !== tier.key));
  };

  const validate = (submitForReview: boolean): string[] => {
    const errors: string[] = [];
    const startsAt = fromLocalInputValue(startsAtInput);
    const startsAtMs = new Date(startsAt).getTime();

    if (title.trim().length < 3) {
      errors.push('Title must be at least 3 characters.');
    }
    if (description.trim().length < 10) {
      errors.push('Description must be at least 10 characters.');
    }
    if (venue.trim().length < 3) {
      errors.push('Venue must be at least 3 characters.');
    }
    if (!startsAtInput || Number.isNaN(startsAtMs)) {
      errors.push('Choose a valid event start date and time.');
    } else if (startsAtMs <= Date.now()) {
      errors.push('The event start must be in the future.');
    }

    if (imageFile) {
      if (imageFile.type !== 'image/jpeg' && imageFile.type !== 'image/jpg') {
        errors.push('The cover image must be a JPEG.');
      }
      if (imageFile.size > MAX_IMAGE_BYTES) {
        errors.push('The cover image must be 2.5 MB or smaller.');
      }
    }

    const usableTiers = tiers.filter(
      (tier) => tier.id || tier.name.trim() || tier.price.trim(),
    );

    if (submitForReview && usableTiers.length === 0) {
      errors.push('Add at least one ticket type before submitting for review.');
    }
    if (submitForReview && !imageFile && !existingImageUrl) {
      errors.push('Add a cover image before submitting for review.');
    }

    usableTiers.forEach((tier, index) => {
      const label = tier.name.trim() || `Ticket type ${index + 1}`;
      const price = Number(tier.price);
      const quantity = Number(tier.quantity);
      const salesOpenAt = new Date(fromLocalInputValue(tier.salesOpenAt)).getTime();
      const earlyPrice = tier.earlyBirdPrice.trim()
        ? Number(tier.earlyBirdPrice)
        : null;
      const earlyEnds = tier.earlyBirdEndsAt
        ? new Date(fromLocalInputValue(tier.earlyBirdEndsAt)).getTime()
        : null;

      if (tier.name.trim().length < 2) {
        errors.push(`${label}: name must be at least 2 characters.`);
      }
      if (!Number.isFinite(price) || price <= 0) {
        errors.push(`${label}: price must be greater than zero.`);
      }
      if (!Number.isInteger(quantity) || quantity < 1) {
        errors.push(`${label}: quantity must be a whole number of at least 1.`);
      }
      if (!tier.salesOpenAt || Number.isNaN(salesOpenAt)) {
        errors.push(`${label}: choose when sales open.`);
      } else if (!Number.isNaN(startsAtMs) && salesOpenAt >= startsAtMs) {
        errors.push(`${label}: sales must open before the event starts.`);
      }
      if ((earlyPrice === null) !== (earlyEnds === null)) {
        errors.push(`${label}: early-bird price and end time must be provided together.`);
      }
      if (earlyPrice !== null && price > 0 && earlyPrice >= price) {
        errors.push(`${label}: early-bird price must be lower than the full price.`);
      }
      if (earlyEnds !== null && !Number.isNaN(salesOpenAt) && earlyEnds <= salesOpenAt) {
        errors.push(`${label}: early bird must end after sales open.`);
      }
      if (earlyEnds !== null && !Number.isNaN(startsAtMs) && earlyEnds >= startsAtMs) {
        errors.push(`${label}: early bird must end before the event starts.`);
      }
    });

    return errors;
  };

  const save = async (submitForReview: boolean) => {
    setError(null);
    setDetails([]);
    const validationErrors = validate(submitForReview);
    if (validationErrors.length > 0) {
      setDetails(validationErrors);
      return;
    }

    setPendingAction(submitForReview ? 'submit' : 'draft');
    let targetId = eventId;
    let workingTiers = [...tiers];
    const usableTiers = workingTiers.filter(
      (tier) => tier.id || tier.name.trim() || tier.price.trim(),
    );

    try {
      const eventPayload = {
        title: title.trim(),
        description: description.trim(),
        venue: venue.trim(),
        startsAt: fromLocalInputValue(startsAtInput),
      };

      if (targetId) {
        await apiRequest<EventResponse>(`/organizer/events/${targetId}`, {
          method: 'PUT',
          body: eventPayload,
        });
      } else {
        const created = await apiRequest<EventResponse>('/organizer/events', {
          method: 'POST',
          body: eventPayload,
        });
        targetId = created.id;
        setEventId(created.id);
      }

      for (const tierId of removedIds) {
        await apiRequest<void>(
          `/organizer/events/${targetId}/ticket-types/${tierId}`,
          { method: 'DELETE' },
        );
      }

      for (let index = 0; index < usableTiers.length; index += 1) {
        const tier = usableTiers[index];
        if (tier.id) {
          await apiRequest<TicketType>(
            `/organizer/events/${targetId}/ticket-types/${tier.id}`,
            { method: 'PUT', body: tierPayload(tier) },
          );
        } else {
          const createdTier = await apiRequest<TicketType>(
            `/organizer/events/${targetId}/ticket-types`,
            { method: 'POST', body: tierPayload(tier) },
          );
          workingTiers = workingTiers.map((item) =>
            item.key === tier.key ? { ...item, id: createdTier.id } : item,
          );
          setTiers(workingTiers);
        }
      }

      if (imageFile && targetId) {
        const form = new FormData();
        form.append('image', imageFile);
        const uploaded = await apiRequest<ImageUploadResponse>(
          `/organizer/events/${targetId}/image`,
          { method: 'POST', body: form },
        );
        setExistingImageUrl(uploaded.imageUrl);
        setImageFile(null);
      }

      const fresh = await apiRequest<EventResponse>(
        `/organizer/events/${targetId}`,
      );
      setTiers(fresh.ticketTypes.map(toTierDraft));
      setExistingImageUrl(fresh.imageUrl);
      setSourceEvent(fresh);
      setRemovedIds([]);

      if (submitForReview) {
        const submitted = await apiRequest<EventResponse>(
          `/organizer/events/${targetId}/submit`,
          { method: 'POST' },
        );
        setSourceEvent(submitted);
        toast.success('Sent for admin review.');
      } else {
        toast.success('Draft saved.');
      }

      navigate(`/organizer/events/${targetId}`);
    } catch (caught) {
      setTiers(workingTiers);
      if (isApiError(caught) && caught.details.length > 0) {
        setDetails(caught.details);
      } else {
        setError(errorMessage(caught));
      }
      if (targetId) {
        setEventId(targetId);
      }
    } finally {
      setPendingAction(null);
    }
  };

  if (loading) {
    return <Loading label="Loading draft" />;
  }

  if (loadError) {
    return (
      <div className="wrap sec">
        <ErrorState message={loadError} onRetry={() => void load()} />
      </div>
    );
  }

  if (sourceEvent && sourceEvent.status !== 'Draft') {
    return (
      <div className="wrap sec">
        <div className="panel">
          <div className="panel-head">
            <h1 className="h3">{sourceEvent.title}</h1>
            <StatusPill status={sourceEvent.status} />
          </div>
          <p className="muted">
            Only drafts can be edited. This event has already been submitted, so
            its content and ticket types are locked.
          </p>
          <div className="form-actions">
            <Link className="btn" to={`/organizer/events/${sourceEvent.id}`}>
              Manage event <ArrowIcon />
            </Link>
            <Link className="btn btn-ghost" to="/organizer">
              Back to my events
            </Link>
          </div>
        </div>
      </div>
    );
  }

  const previewUrl = imagePreview ?? existingImageUrl;

  return (
    <div className="wrap sec">
      <Link className="backlink" to={eventId ? `/organizer/events/${eventId}` : '/organizer'}>
        <ArrowIcon size={16} /> {eventId ? 'Back to event' : 'My events'}
      </Link>

      <div className="page-head">
        <div>
          <span className="mono muted" style={{ fontSize: '.7rem' }}>
            {eventId ? 'Draft editor' : 'New draft'}
          </span>
          <h1 className="h2">{eventId ? 'Edit event' : 'Create event'}</h1>
          <p className="muted" style={{ marginTop: 8 }}>
            Times are in WAT. The event locks after you submit it for review.
          </p>
        </div>
        <span className="status s-draft">Draft</span>
      </div>

      <div className="co">
        <div>
          <section className="panel">
            <div className="panel-head">
              <h2 className="h3">The basics</h2>
            </div>
            <div className="form-grid">
              <div className="field full">
                <label htmlFor="title">Event title</label>
                <input
                  id="title"
                  type="text"
                  value={title}
                  maxLength={200}
                  onChange={(inputEvent) => setTitle(inputEvent.target.value)}
                  placeholder="e.g. Lagoon Sessions: Live at the waterfront"
                  required
                />
              </div>
              <div className="field">
                <label htmlFor="venue">Venue</label>
                <input
                  id="venue"
                  type="text"
                  value={venue}
                  maxLength={300}
                  onChange={(inputEvent) => setVenue(inputEvent.target.value)}
                  placeholder="e.g. Freedom Park, Lagos"
                  required
                />
              </div>
              <div className="field">
                <label htmlFor="startsAt">Starts (WAT)</label>
                <input
                  id="startsAt"
                  type="datetime-local"
                  value={startsAtInput}
                  onChange={(inputEvent) => setStartsAtInput(inputEvent.target.value)}
                  required
                />
              </div>
              <div className="field full">
                <label htmlFor="description">Description</label>
                <textarea
                  id="description"
                  value={description}
                  maxLength={4000}
                  onChange={(inputEvent) => setDescription(inputEvent.target.value)}
                  placeholder="Tell guests what the night is about."
                  required
                />
                <span className="hint">
                  {description.trim().length}/10 minimum characters.
                </span>
              </div>
            </div>
          </section>

          <section className="panel">
            <div className="panel-head">
              <h2 className="h3">Ticket types</h2>
              <button
                type="button"
                className="pill"
                onClick={() => setTiers((current) => [...current, newTier()])}
              >
                + Add ticket type
              </button>
            </div>

            {tiers.length === 0 ? (
              <div className="note">
                No ticket types yet. Add at least one to submit for review.
              </div>
            ) : null}

            {tiers.map((tier, index) => (
              <div
                className="panel"
                key={tier.key}
                style={{ background: 'var(--surface-2)', marginTop: 14 }}
              >
                <div className="panel-head">
                  <h3 className="h3" style={{ fontSize: '1rem' }}>
                    {tier.name.trim() || `Ticket type ${index + 1}`}
                  </h3>
                  <button
                    type="button"
                    className="pill"
                    onClick={() => removeTier(tier)}
                  >
                    Remove
                  </button>
                </div>
                <div className="form-grid">
                  <div className="field full">
                    <label>Name</label>
                    <input
                      type="text"
                      value={tier.name}
                      maxLength={100}
                      onChange={(inputEvent) =>
                        updateTier(tier.key, { name: inputEvent.target.value })
                      }
                      placeholder="e.g. Early bird, General, VIP"
                    />
                  </div>
                  <div className="field">
                    <label>Price (₦)</label>
                    <input
                      type="number"
                      min="0.01"
                      step="0.01"
                      value={tier.price}
                      onChange={(inputEvent) =>
                        updateTier(tier.key, { price: inputEvent.target.value })
                      }
                      placeholder="12500"
                    />
                  </div>
                  <div className="field">
                    <label>Quantity</label>
                    <input
                      type="number"
                      min="1"
                      step="1"
                      value={tier.quantity}
                      onChange={(inputEvent) =>
                        updateTier(tier.key, { quantity: inputEvent.target.value })
                      }
                    />
                  </div>
                  <div className="field">
                    <label>Sales open (WAT)</label>
                    <input
                      type="datetime-local"
                      value={tier.salesOpenAt}
                      onChange={(inputEvent) =>
                        updateTier(tier.key, { salesOpenAt: inputEvent.target.value })
                      }
                    />
                  </div>
                  <div className="field">
                    <label>Early-bird price (₦)</label>
                    <input
                      type="number"
                      min="0.01"
                      step="0.01"
                      value={tier.earlyBirdPrice}
                      onChange={(inputEvent) =>
                        updateTier(tier.key, {
                          earlyBirdPrice: inputEvent.target.value,
                        })
                      }
                      placeholder="Optional"
                    />
                  </div>
                  <div className="field">
                    <label>Early bird ends (WAT)</label>
                    <input
                      type="datetime-local"
                      value={tier.earlyBirdEndsAt}
                      onChange={(inputEvent) =>
                        updateTier(tier.key, {
                          earlyBirdEndsAt: inputEvent.target.value,
                        })
                      }
                    />
                  </div>
                </div>
                {tier.id ? (
                  <p className="fine">Existing ticket type · edits save in place.</p>
                ) : null}
              </div>
            ))}
          </section>
        </div>

        <aside>
          <section className="panel">
            <div className="panel-head">
              <h2 className="h3">Cover image</h2>
            </div>
            <div
              className="ev-photo ph"
              style={{
                aspectRatio: '4/3',
                backgroundImage: previewUrl ? `url("${previewUrl}")` : undefined,
              }}
            >
              {!previewUrl ? <span className="ph-label">JPEG REQUIRED</span> : null}
            </div>
            <div className="field" style={{ marginTop: 16 }}>
              <label htmlFor="cover">Upload JPEG</label>
              <input
                id="cover"
                type="file"
                accept="image/jpeg,image/jpg"
                onChange={(inputEvent) => {
                  const file = inputEvent.target.files?.[0] ?? null;
                  setImageFile(file);
                  if (file && file.size > MAX_IMAGE_BYTES) {
                    setError('The cover image must be 2.5 MB or smaller.');
                  } else {
                    setError(null);
                  }
                }}
              />
              <span className="hint">
                Landscape works best. JPEG only, 2.5 MB maximum.
              </span>
            </div>
          </section>

          <section className="panel">
            <h2 className="h3">Publish checklist</h2>
            <ul className="tk-list" style={{ marginTop: 14 }}>
              <li>Title, description, venue and future start time</li>
              <li>At least one ticket type with a price and stock</li>
              <li>A JPEG cover image</li>
              <li>Sales open and early bird end before the event</li>
            </ul>
            {details.length > 0 ? (
              <div className="alert" role="alert" style={{ marginTop: 18 }}>
                Check these fields:
                <ul>
                  {details.map((detail) => (
                    <li key={detail}>{detail}</li>
                  ))}
                </ul>
              </div>
            ) : null}
            {error ? (
              <div className="alert" role="alert" style={{ marginTop: 18 }}>
                {error}
              </div>
            ) : null}
            <button
              type="button"
              className="btn btn-ghost btn-block"
              style={{ marginTop: 18 }}
              disabled={pendingAction !== null}
              onClick={() => void save(false)}
            >
              {pendingAction === 'draft' ? 'Saving…' : 'Save draft'}
            </button>
            <button
              type="button"
              className="btn btn-lime btn-block"
              style={{ marginTop: 10 }}
              disabled={pendingAction !== null}
              onClick={() => void save(true)}
            >
              {pendingAction === 'submit' ? 'Submitting…' : 'Save and submit for review'}
            </button>
            <p className="fine">
              You can keep editing a draft. Once submitted, the content is locked
              until an admin has reviewed it.
            </p>
          </section>
        </aside>
      </div>
    </div>
  );
}
