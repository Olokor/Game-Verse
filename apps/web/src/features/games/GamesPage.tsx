import { Link, useSearchParams } from 'react-router';
import { CloudOff, Gamepad2 } from 'lucide-react';
import { useCatalog } from '../../data/api';
import type { Category, GameSummary } from '../../data/types';
import { Button, buttonClasses } from '../../components/ui/Button';
import { GameCard, GameCardSkeleton } from '../../components/ui/GameCard';
import { categoryLabel } from '../../components/ui/tokens';
import { Section, SectionMessage } from '../home/Section';

/**
 * /games: the whole catalog, playable games first, then coming soon. `?category=board` filters (Home's category tiles
 * link here). TODO(gameverse): search and the full category browse screen (DESIGN_SYSTEM.md section 7, screen 3).
 */
export function GamesPage() {
  const catalog = useCatalog();
  const [params] = useSearchParams();
  const category = params.get('category') as Category | null;
  const filtered = catalog.data?.filter((g) => !category || g.category === category);
  const title = category && category in categoryLabel ? `${categoryLabel[category]} games` : 'Games';

  return (
    <div className="flex flex-col gap-40">
      <header className="flex flex-col gap-8">
        <h1 className="type-display text-headline font-bold">{title}</h1>
        {category && (
          <Link to="/games" className="w-fit text-body-sm font-bold text-text-secondary hover:text-text-primary">
            Show all categories
          </Link>
        )}
      </header>

      {catalog.isPending ? (
        <div role="status" aria-busy="true" aria-label="Loading games" className="grid grid-cols-2 gap-12 sm:grid-cols-3 lg:grid-cols-4">
          {Array.from({ length: 8 }, (_, i) => <GameCardSkeleton key={i} />)}
        </div>
      ) : catalog.isError ? (
        <SectionMessage
          icon={<CloudOff className="size-20" />}
          title="Couldn't load the games"
          body="Check your connection and try again."
          action={
            <Button variant="secondary" size="sm" loading={catalog.isFetching} onClick={() => void catalog.refetch()}>
              Try again
            </Button>
          }
        />
      ) : filtered && filtered.length > 0 ? (
        <>
          <GameGrid id="playable" title="Play now" games={filtered.filter((g) => g.playable)} />
          <GameGrid id="coming" title="Coming soon" games={filtered.filter((g) => !g.playable)} />
        </>
      ) : (
        <SectionMessage
          icon={<Gamepad2 className="size-20" />}
          title="No games here yet"
          body="Games in this category are on their way."
          action={<Link to="/games" className={buttonClasses({ variant: 'secondary', size: 'sm' })}>All games</Link>}
        />
      )}
    </div>
  );
}

function GameGrid({ id, title, games }: { id: string; title: string; games: GameSummary[] }) {
  if (games.length === 0) return null;
  return (
    <Section id={id} title={title}>
      <ul className="grid grid-cols-2 gap-12 sm:grid-cols-3 lg:grid-cols-4">
        {games.map((g) => (
          <li key={g.id}>
            <GameCard game={g} />
          </li>
        ))}
      </ul>
    </Section>
  );
}
