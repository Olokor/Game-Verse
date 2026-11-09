import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { CloudOff, Gamepad2, LayoutGrid, ScrollText, UserPlus } from 'lucide-react';
import { homeFeedKey, useHomeFeed } from '../../data/api';
import type { CategorySummary, ContinueEntry, Friend, HomeFeed, Quest } from '../../data/types';
import { cn } from '../../lib/cn';
import { usePlayerStore } from '../../stores/usePlayerStore';
import { Button, buttonClasses } from '../../components/ui/Button';
import { GameCard, GameCardSkeleton } from '../../components/ui/GameCard';
import { PlayerChip, PlayerChipSkeleton } from '../../components/ui/PlayerChip';
import { QuestCard, QuestCardSkeleton } from '../../components/ui/QuestCard';
import { Skeleton } from '../../components/ui/Skeleton';
import { XPBar } from '../../components/ui/XPBar';
import { categoryStyle } from '../../components/ui/tokens';
import { Section, SectionMessage } from './Section';

/**
 * Home: hero featured game, continue playing, daily quests, categories, friends online (DESIGN_SYSTEM.md section 7).
 * Phones stack the sections; xl screens split into a main column and a side column. `contents` lets the same
 * elements be reordered on small screens and grouped into columns on large ones.
 */
export function HomeScreen() {
  const feed = useHomeFeed();

  return (
    <>
      <h1 className="sr-only">Home</h1>
      <Greeting />
      {feed.isPending ? (
        <HomeSkeleton />
      ) : feed.isError ? (
        <SectionMessage
          icon={<CloudOff className="size-20" />}
          title="Couldn't load your home feed"
          body="Check your connection and try again. Your games and coins are safe."
          action={
            <Button variant="secondary" size="sm" loading={feed.isFetching} onClick={() => void feed.refetch()}>
              Try again
            </Button>
          }
        />
      ) : (
        <HomeLayout feed={feed.data} />
      )}
    </>
  );
}

function Greeting() {
  const { displayName, level, xp, xpRequired } = usePlayerStore();
  return (
    <div className="mb-24 flex flex-col gap-16 lg:hidden">
      <p className="type-display text-headline font-bold">
        Welcome back, {displayName}
      </p>
      <XPBar level={level} xp={xp} xpRequired={xpRequired} />
    </div>
  );
}

function Columns({ main, side }: { main: ReactNode; side: ReactNode }) {
  return (
    <div className="flex flex-col gap-40 xl:flex-row xl:items-start">
      <div className="contents xl:flex xl:min-w-0 xl:flex-[2] xl:flex-col xl:gap-40">{main}</div>
      <div className="contents xl:flex xl:min-w-0 xl:flex-1 xl:flex-col xl:gap-40">{side}</div>
    </div>
  );
}

function HomeLayout({ feed }: { feed: HomeFeed }) {
  return (
    <Columns
      main={
        <>
          {feed.featured && (
            <div className="order-1 xl:order-none">
              <GameCard game={feed.featured} variant="hero" />
            </div>
          )}
          <ContinuePlaying entries={feed.continuePlaying} className="order-2 xl:order-none" />
          <Categories categories={feed.categories} className="order-4 xl:order-none" />
        </>
      }
      side={
        <>
          <DailyQuests quests={feed.quests} resetInMinutes={feed.questsResetInMinutes} className="order-3 xl:order-none" />
          <FriendsOnline friends={feed.friends} className="order-5 xl:order-none" />
        </>
      }
    />
  );
}

function ContinuePlaying({ entries, className }: { entries: ContinueEntry[]; className?: string }) {
  return (
    <Section id="continue" title="Continue playing" action={entries.length ? { label: 'See all', to: '/games' } : undefined} className={className}>
      {entries.length === 0 ? (
        <SectionMessage
          icon={<Gamepad2 className="size-20" />}
          title="Nothing in progress"
          body="Games you start show up here so you can jump back in."
          action={<Link to="/games" className={buttonClasses({ variant: 'secondary', size: 'sm' })}>Browse games</Link>}
        />
      ) : (
        // Phones and tablets: a swipeable row that bleeds to the screen edge. lg+: a 4-column grid, so nothing is
        // clipped for mouse users; extra entries are behind "See all". py-4 leaves room for focus rings.
        <ul className="scroll-row -mx-gutter-mobile flex gap-12 overflow-x-auto scroll-px-gutter-mobile px-gutter-mobile py-4 sm:-mx-gutter-tablet sm:scroll-px-gutter-tablet sm:px-gutter-tablet lg:mx-0 lg:grid lg:grid-cols-4 lg:overflow-visible lg:px-0 lg:py-0">
          {entries.map((e, i) => (
            <li key={e.game.id} className={cn('w-2/5 shrink-0 sm:w-1/4 lg:w-auto', i >= 4 && 'lg:hidden')}>
              <GameCard game={e.game} subtitle={e.status} yourTurn={e.yourTurn} />
            </li>
          ))}
        </ul>
      )}
    </Section>
  );
}

function formatReset(minutes: number) {
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return h > 0 ? `${h}h ${m}m` : `${m}m`;
}

function DailyQuests({ quests, resetInMinutes, className }: { quests: Quest[]; resetInMinutes: number; className?: string }) {
  const queryClient = useQueryClient();
  const addCoins = usePlayerStore((s) => s.addCoins);
  const addXp = usePlayerStore((s) => s.addXp);

  // TODO(gameverse): mock claim. Real claims go through the progression API and the coin ledger (Prompts 2.1, 2.2).
  const claim = useMutation({
    mutationFn: async (id: string) => {
      const quest = quests.find((q) => q.id === id);
      if (!quest) throw new Error('unknown quest');
      return quest;
    },
    onSuccess: (quest) => {
      if (quest.reward.kind === 'coin') addCoins(quest.reward.amount);
      else addXp(quest.reward.amount);
      queryClient.setQueryData<HomeFeed>(homeFeedKey, (old) =>
        old && { ...old, quests: old.quests.map((q) => (q.id === quest.id ? { ...q, claimed: true } : q)) },
      );
    },
  });

  return (
    <Section
      id="quests"
      title="Daily quests"
      aside={<span className="type-numeric text-body-sm text-text-secondary">Resets in {formatReset(resetInMinutes)}</span>}
      className={className}
    >
      {quests.length === 0 ? (
        <SectionMessage
          icon={<ScrollText className="size-20" />}
          title="All done for today"
          body={`New quests arrive in ${formatReset(resetInMinutes)}.`}
        />
      ) : (
        <ul className="flex flex-col gap-8">
          {quests.map((q) => (
            <li key={q.id}>
              <QuestCard
                quest={q}
                onClaim={(id) => claim.mutate(id)}
                claiming={claim.isPending && claim.variables === q.id}
                error={claim.isError && claim.variables === q.id}
              />
            </li>
          ))}
        </ul>
      )}
    </Section>
  );
}

function Categories({ categories, className }: { categories: CategorySummary[]; className?: string }) {
  return (
    <Section id="categories" title="Categories" className={className}>
      {categories.length === 0 ? (
        <SectionMessage
          icon={<LayoutGrid className="size-20" />}
          title="No categories yet"
          body="Games are on their way. Check back soon."
        />
      ) : (
        <ul className="grid grid-cols-2 gap-8 sm:grid-cols-4">
          {categories.map((c) => {
            const style = categoryStyle[c.category];
            return (
              <li key={c.category}>
                {/* TODO(gameverse): category browse screen (DESIGN_SYSTEM.md section 7, screen 3). */}
                <Link
                  to={`/games?category=${c.category}`}
                  className="pressable group flex min-h-56 items-center gap-12 rounded-md border border-border-subtle bg-bg-surface px-12 hover:border-border-strong"
                >
                  <style.icon className={cn('size-20 shrink-0', style.text)} aria-hidden="true" />
                  <span className="flex min-w-0 flex-col">
                    <span className="truncate text-body-sm font-bold text-text-primary">{c.label}</span>
                    <span className="type-numeric text-body-sm text-text-secondary">
                      {c.gameCount} {c.gameCount === 1 ? 'game' : 'games'}
                    </span>
                  </span>
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </Section>
  );
}

function FriendsOnline({ friends, className }: { friends: Friend[]; className?: string }) {
  const visible = friends.filter((f) => f.presence !== 'offline');
  return (
    <Section
      id="friends"
      title="Friends online"
      aside={<span className="type-numeric text-body-sm text-text-secondary">{visible.length} of {friends.length} online</span>}
      action={{ label: 'All friends', to: '/social' }}
      className={className}
    >
      {visible.length === 0 ? (
        <SectionMessage
          icon={<UserPlus className="size-20" />}
          title={friends.length === 0 ? 'Play with friends' : 'Nobody online right now'}
          body={friends.length === 0 ? 'Add friends to see when they are online and invite them to a game.' : 'Invite someone to a game; they will get a notification.'}
          action={<Link to="/social" className={buttonClasses({ variant: 'secondary', size: 'sm' })}>Find friends</Link>}
        />
      ) : (
        <ul className="flex flex-col divide-y divide-border-subtle rounded-lg border border-border-subtle bg-bg-surface px-16">
          {visible.map((f) => (
            <li key={f.id} className="py-12">
              <PlayerChip
                displayName={f.displayName}
                rank={f.rank}
                presence={f.presence}
                activity={f.activity}
                action={
                  f.presence === 'online' ? (
                    // TODO(gameverse): sends a room invite once private rooms land (Prompt 1.4).
                    <Button variant="ghost" size="sm" aria-label={`Invite ${f.displayName}`}>Invite</Button>
                  ) : undefined
                }
              />
            </li>
          ))}
        </ul>
      )}
    </Section>
  );
}

function HomeSkeleton() {
  return (
    <div role="status" aria-busy="true" aria-label="Loading home">
      <span className="sr-only">Loading home</span>
      <Columns
        main={
          <>
            <GameCardSkeleton variant="hero" className="order-1 xl:order-none" />
            <div className="order-2 flex flex-col gap-16 xl:order-none">
              <Skeleton className="h-24 w-1/3" />
              <div className="flex gap-12 overflow-hidden">
                {[0, 1, 2, 3].map((i) => (
                  <GameCardSkeleton key={i} className="w-2/5 shrink-0 sm:w-1/4" />
                ))}
              </div>
            </div>
          </>
        }
        side={
          <>
            <div className="order-3 flex flex-col gap-8 xl:order-none">
              <Skeleton className="mb-8 h-24 w-1/2" />
              {[0, 1, 2].map((i) => (
                <QuestCardSkeleton key={i} />
              ))}
            </div>
            <div className="order-5 flex flex-col gap-16 rounded-lg border border-border-subtle bg-bg-surface p-16 xl:order-none">
              {[0, 1, 2].map((i) => (
                <PlayerChipSkeleton key={i} />
              ))}
            </div>
          </>
        }
      />
    </div>
  );
}
