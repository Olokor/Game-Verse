import { useState, type ReactNode } from 'react';
import { Heart, Play, Trash2 } from 'lucide-react';
import { Button, type ButtonSize, type ButtonVariant } from '../../components/ui/Button';
import { CurrencyPill } from '../../components/ui/CurrencyPill';
import { GameCard, GameCardSkeleton } from '../../components/ui/GameCard';
import { PlayerChip, PlayerChipSkeleton } from '../../components/ui/PlayerChip';
import { QuestCard, QuestCardSkeleton } from '../../components/ui/QuestCard';
import { Skeleton, SkeletonText } from '../../components/ui/Skeleton';
import { XPBar } from '../../components/ui/XPBar';
import { mockGames, mockHomeFeed } from '../../data/mock/homeFeed';
import type { Presence, RankTier } from '../../data/types';
import { usePlayerStore } from '../../stores/usePlayerStore';

/** /dev/components: every core component in every state. Development and screenshot builds only. */
export function ComponentsPage() {
  return (
    <div className="flex flex-col gap-40">
      <header className="flex flex-col gap-8">
        <span className="type-label text-brand-volt">Dev</span>
        <h1 className="type-display text-display font-bold">Components</h1>
        <p className="max-w-2xl text-body text-text-secondary">
          Every core component from DESIGN_SYSTEM.md section 6 in every state. Values come only from design tokens.
          Press and hover states are live; focus with Tab to see focus rings.
        </p>
      </header>
      <ButtonsDemo />
      <CurrencyDemo />
      <GameCardsDemo />
      <PlayerChipsDemo />
      <QuestsDemo />
      <SkeletonsDemo />
    </div>
  );
}

function Demo({ title, note, children }: { title: string; note?: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-16 border-t border-border-subtle pt-24">
      <div className="flex flex-col gap-4">
        <h2 className="type-display text-title font-bold">{title}</h2>
        {note && <p className="text-body-sm text-text-secondary">{note}</p>}
      </div>
      {children}
    </section>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-8">
      <span className="type-label text-text-muted">{label}</span>
      <div className="flex flex-wrap items-center gap-12">{children}</div>
    </div>
  );
}

const variants: ButtonVariant[] = ['primary', 'secondary', 'ghost', 'danger'];
const sizes: ButtonSize[] = ['sm', 'md', 'lg'];

function ButtonsDemo() {
  const [busy, setBusy] = useState(false);
  return (
    <Demo title="Button" note="primary, secondary, ghost, danger. sm, md, lg (all at least 44px tall). Default, disabled, loading.">
      {variants.map((v) => (
        <Row key={v} label={v}>
          {sizes.map((s) => (
            <Button key={s} variant={v} size={s} icon={v === 'danger' ? <Trash2 className="size-16" /> : v === 'primary' ? <Play className="size-16 fill-current" /> : undefined}>
              {v === 'danger' ? 'Leave match' : v === 'primary' ? 'Play' : 'Invite'}
            </Button>
          ))}
          <Button variant={v} disabled>Disabled</Button>
          <Button variant={v} loading>Loading</Button>
          <Button variant={v} pill size="sm" icon={<Heart className="size-16" />}>Pill</Button>
        </Row>
      ))}
      <Row label="interactive loading">
        <Button
          loading={busy}
          onClick={() => {
            setBusy(true);
            setTimeout(() => setBusy(false), 1500);
          }}
        >
          Find match
        </Button>
        <Button variant="primary" size="lg" fullWidth className="sm:w-auto">
          Full width on phones
        </Button>
      </Row>
    </Demo>
  );
}

function CurrencyDemo() {
  const { coins, gems, level, xp, xpRequired, addCoins, addXp } = usePlayerStore();
  return (
    <Demo title="CurrencyPill and XPBar" note="Amounts count up on change and briefly highlight gains. Reduced motion jumps straight to the value.">
      <Row label="currency">
        <CurrencyPill currency="coin" amount={coins} />
        <CurrencyPill currency="gem" amount={gems} />
        <CurrencyPill currency="coin" amount={coins} onClick={() => addCoins(250)} />
        <CurrencyPill currency="coin" amount={0} loading />
      </Row>
      <Row label="actions">
        <Button variant="secondary" size="sm" onClick={() => addCoins(250)}>+250 coins</Button>
        <Button variant="secondary" size="sm" onClick={() => addCoins(-500)}>-500 coins</Button>
        <Button variant="secondary" size="sm" onClick={() => addXp(300)}>+300 XP</Button>
      </Row>
      <div className="grid gap-24 sm:grid-cols-2">
        <XPBar level={level} xp={xp} xpRequired={xpRequired} />
        <XPBar level={level} xp={xp} xpRequired={xpRequired} compact />
        <XPBar level={1} xp={0} xpRequired={500} />
        <XPBar level={0} xp={0} xpRequired={0} loading />
      </div>
    </Demo>
  );
}

function GameCardsDemo() {
  const games = Object.values(mockGames);
  return (
    <Demo title="GameCard" note="hero (featured, one soft glow), standard (3:4), compact (row). Badges: New, Event. Skeletons for each.">
      <GameCard game={mockGames.ludo!} variant="hero" />
      <div className="grid grid-cols-2 gap-12 sm:grid-cols-4">
        {games.slice(0, 4).map((g) => (
          <GameCard key={g.id} game={g} yourTurn={g.id === 'whot'} />
        ))}
      </div>
      <div className="grid gap-8 sm:grid-cols-2">
        {games.slice(4).map((g) => (
          <GameCard key={g.id} game={g} variant="compact" />
        ))}
      </div>
      <Row label="loading">
        <span className="sr-only">Skeletons</span>
      </Row>
      <GameCardSkeleton variant="hero" />
      <div className="grid grid-cols-2 gap-12 sm:grid-cols-4">
        <GameCardSkeleton />
        <GameCardSkeleton />
        <GameCardSkeleton variant="compact" className="col-span-2" />
      </div>
    </Demo>
  );
}

const ranks: RankTier[] = ['bronze', 'silver', 'gold', 'platinum', 'diamond', 'legend'];
const presences: Presence[] = ['online', 'in-game', 'offline'];

function PlayerChipsDemo() {
  return (
    <Demo title="PlayerChip" note="Every rank tier (icon + name + frame color) and presence (dot + text).">
      <div className="grid gap-16 rounded-lg border border-border-subtle bg-bg-surface p-16 sm:grid-cols-2 lg:grid-cols-3">
        {ranks.map((rank, i) => {
          const presence = presences[i % presences.length] ?? 'online';
          return (
            <PlayerChip
              key={rank}
              displayName={['Adaeze', 'Kunle', 'Tobi', 'Halima', 'Emeka', 'Zainab'][i] ?? 'Player'}
              rank={rank}
              presence={presence}
              activity={presence === 'in-game' ? 'Ludo' : undefined}
              action={presence === 'online' ? <Button variant="ghost" size="sm">Invite</Button> : undefined}
            />
          );
        })}
        <PlayerChip displayName="Wisdom" rank="gold" hideStatus />
        <PlayerChipSkeleton />
      </div>
    </Demo>
  );
}

function QuestsDemo() {
  const [quests, setQuests] = useState(mockHomeFeed.quests);
  return (
    <Demo title="QuestCard" note="active, claimable (volt outline + Claim), claiming, claim failed, claimed.">
      <div className="grid gap-8 lg:grid-cols-2">
        {quests.map((q) => (
          <QuestCard key={q.id} quest={q} onClaim={(id) => setQuests((all) => all.map((x) => (x.id === id ? { ...x, claimed: true } : x)))} />
        ))}
        <QuestCard quest={{ ...mockHomeFeed.quests[0]!, id: 'busy', progress: 2 }} claiming />
        <QuestCard quest={{ ...mockHomeFeed.quests[0]!, id: 'failed', progress: 2 }} error />
        <QuestCardSkeleton />
      </div>
    </Demo>
  );
}

function SkeletonsDemo() {
  return (
    <Demo title="Skeleton" note="Loading uses skeletons, never spinners. Static under reduced motion.">
      <div className="grid gap-16 sm:grid-cols-3">
        <Skeleton className="h-72" />
        <SkeletonText lines={3} />
        <div className="flex items-center gap-12">
          <Skeleton className="size-40 rounded-pill" />
          <SkeletonText lines={2} className="flex-1" />
        </div>
      </div>
    </Demo>
  );
}
